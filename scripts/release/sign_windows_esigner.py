#!/usr/bin/env python3
"""Manually sign a prepared Windows release through local eSigner CKA."""

import argparse
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

from safe_extract_windows_release import ArchivePolicyError, extract_archive
from windows_signing_budget import (
    AUTHORED, MSI, SigningBudget, SigningError, checked_path, digest, read_json,
)

SCRIPTS = Path(__file__).resolve().parent
REPO = SCRIPTS.parent.parent
ARCHIVE = "labtether-agent-win-x64.zip"
UNSIGNED_ARCHIVE = "labtether-agent-win-x64-unsigned.zip"
UNSIGNED_MSI = "labtether-agent-win-x64-unsigned.msi"
PROVENANCE = "release-provenance.json"
SIGNED_MANIFEST = "signed-payloads.json"


def host(action, **values):
    """Only this fixed adapter may access the Windows signing tools.

    Capture all native output: it can include certificate selectors. Keep the
    selector out of launcher arguments, console logs, errors and receipts.
    """
    powershell = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
    try:
        result = subprocess.run(
            [str(powershell), "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
             str(SCRIPTS / "windows-esigner-host.ps1")],
            input=json.dumps({"action": action, **values}), encoding="utf-8",
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=660,
        )
        if result.returncode or json.loads(result.stdout.strip()).get("ok") is not True:
            raise SigningError("Local Windows signing check failed; no automatic retry is permitted")
    except (OSError, subprocess.TimeoutExpired, ValueError) as error:
        raise SigningError("Local Windows signing check did not complete; inspect the saved reservation") from error


def external(path, repositories):
    path = Path(os.path.abspath(path))
    if any(path == root or root in path.parents for root in repositories):
        raise SigningError("Signing state and assets must stay outside both source repositories")
    return path


def clean_source(repo, tag):
    def git(*args):
        result = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, timeout=30)
        if result.returncode:
            raise SigningError("Could not verify the clean tagged source")
        return result.stdout.strip()
    head = git("rev-parse", "HEAD")
    if (git("status", "--porcelain=v1", "--untracked-files=all")
            or head != git("rev-parse", f"refs/tags/{tag}^{{commit}}")
            or not re.fullmatch(r"[0-9a-f]{40}", head)):
        raise SigningError("Both source checkouts must be clean and match the exact release tag")
    host("source", path=str(repo))
    return head


def metadata(root, name):
    path = checked_path(root / name)
    if path.stat().st_size > 16 * 1024 * 1024:
        raise SigningError("Release metadata exceeds its size limit")
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, UnicodeError) as error:
        raise SigningError("Release metadata is invalid") from error
    if not isinstance(value, dict):
        raise SigningError("Release metadata must be an object")
    return value


def records_by_path(records, actual):
    if not isinstance(records, list) or not 1 <= len(records) <= 2048:
        raise SigningError("Release file manifest is outside its count limit")
    result = {}
    for item in records:
        if not isinstance(item, dict):
            raise SigningError("Release file manifest contains an invalid record")
        name, size, sha = item.get("path"), item.get("size"), item.get("sha256")
        if (not isinstance(name, str) or name not in actual or name in result
                or type(size) is not int or not 0 <= size <= 268435456
                or not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{64}", sha)):
            raise SigningError("Release file manifest does not match the extracted files")
        result[name] = item
    return result


def validate_payload(root, candidate, *, signed=False):
    actual = {path.relative_to(root).as_posix() for path in root.rglob("*") if path.is_file()}
    proof = metadata(root, PROVENANCE)
    if (type(proof.get("schema_version")) is not int or proof["schema_version"] != 1
            or proof.get("payload") != "labtether-agent-win-x64-unsigned"
            or any(proof.get(key) != candidate[key] for key in ("tag", "wrapper_commit", "agent_commit"))):
        raise SigningError("Release provenance does not match the clean tagged sources")
    published = records_by_path(proof.get("published_files"), actual - {PROVENANCE, SIGNED_MANIFEST})
    expected = set(published) | {PROVENANCE} | ({SIGNED_MANIFEST} if signed else set())
    if actual != expected or any(path.is_dir() and not any(path.iterdir()) for path in root.rglob("*")):
        raise SigningError("Release archive contains unexpected files or empty directories")
    total = sum(item["size"] for item in published.values())
    if (type(proof.get("published_file_count")) is not int or proof["published_file_count"] != len(published)
            or type(proof.get("published_bytes")) is not int or proof["published_bytes"] != total
            or not 1 <= total <= 536870912):
        raise SigningError("Release provenance count or byte total is invalid")
    authored = records_by_path(proof.get("authored_payloads"), actual)
    if set(authored) != set(AUTHORED) or any(authored[name] != published.get(name) for name in AUTHORED):
        raise SigningError("Release provenance must contain the exact three authored payloads")
    expected_records = dict(published)
    if signed:
        manifest = metadata(root, SIGNED_MANIFEST)
        signed_records = records_by_path(manifest.get("payloads"), actual)
        if type(manifest.get("schema_version")) is not int or manifest["schema_version"] != 1 or set(signed_records) != set(AUTHORED):
            raise SigningError("Signed manifest must contain the exact three authored payloads")
        expected_records.update(signed_records)
    for name, record in expected_records.items():
        path = checked_path(root / name)
        if path.stat().st_size != record["size"] or digest(path) != record["sha256"]:
            raise SigningError("Release payload bytes differ from their manifest")
    if (root / "AGENT_VERSION").read_text(encoding="utf-8").strip() != candidate["tag"]:
        raise SigningError("Bundled agent version differs from the release tag")


def unpack(archive, destination):
    before = digest(archive)
    if archive.stat().st_size > 600 * 1024 * 1024:
        raise SigningError("Release archive exceeds the compressed size limit")
    destination.mkdir()
    extract_archive(archive, destination)
    if digest(archive) != before:
        raise SigningError("Release archive changed during extraction")
    return before


def checksum(path):
    (path.parent / (path.name + ".sha256")).write_text(f"{digest(path)}  {path.name}\n", encoding="ascii")


def check_assets(directory):
    directory = checked_path(directory, directory=True)
    if {path.name for path in directory.iterdir()} != {ARCHIVE, ARCHIVE + ".sha256"}:
        raise SigningError("The signed ZIP stage must contain exactly its archive and checksum")
    archive = checked_path(directory / ARCHIVE)
    check = checked_path(directory / (ARCHIVE + ".sha256"))
    if check.read_text(encoding="ascii").strip() != f"{digest(archive)}  {ARCHIVE}":
        raise SigningError("The signed ZIP checksum does not match")
    return archive


def write_archive(payload, target):
    with zipfile.ZipFile(target, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(payload.rglob("*")):
            if path.is_file():
                archive.write(checked_path(path), path.relative_to(payload).as_posix())
    checksum(target)


def place_assets(staging, output):
    if any(output.iterdir()):
        raise SigningError("Release output directory must remain empty")
    owned = []
    try:
        for source in sorted(staging.iterdir()):
            destination = output / source.name
            with source.open("rb") as original, destination.open("xb") as target:
                owned.append(destination)
                shutil.copyfileobj(original, target)
                target.flush()
                os.fsync(target.fileno())
            if digest(source) != digest(destination):
                raise SigningError("Placed release bytes differ from verified output")
    except BaseException:
        for path in owned:
            path.unlink()
        raise


def archive_stage(args, candidate, budget, scratch, sign, verify):
    payload = scratch / "payload"
    if unpack(args.unsigned_archive, payload) != candidate["unsigned_archive_sha256"]:
        raise SigningError("Unsigned archive changed after candidate selection")
    validate_payload(payload, candidate)
    for name in AUTHORED:
        host("unsigned", path=str(payload / name))
    for name in AUTHORED:
        cached = budget.sign_once(name, payload / name, sign, verify)
        shutil.copyfile(cached, payload / name)
    manifest = {"schema_version": 1, "payloads": [
        {"path": name, "size": (payload / name).stat().st_size, "sha256": digest(payload / name)}
        for name in AUTHORED
    ]}
    (payload / SIGNED_MANIFEST).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    staging = scratch / "assets"
    staging.mkdir()
    write_archive(payload, staging / ARCHIVE)
    extracted = scratch / "archive-check"
    unpack(staging / ARCHIVE, extracted)
    validate_payload(extracted, candidate, signed=True)
    for name in AUTHORED:
        verify(extracted / name)
    return staging


def msi_stage(args, candidate, budget, scratch, sign, verify):
    archive = check_assets(args.signed_assets)
    payload = scratch / "payload"
    unpack(archive, payload)
    validate_payload(payload, candidate, signed=True)
    for name in AUTHORED:
        cached, _ = budget.completed(name, verify)
        if digest(cached) != digest(payload / name):
            raise SigningError("Signed ZIP does not contain this candidate's signed payloads")
        verify(payload / name)
    # The build output can be regenerated. Check and sign one private snapshot,
    # never reopen that external output after its content check has passed.
    unsigned_msi = scratch / UNSIGNED_MSI
    input_hash = digest(args.unsigned_msi)
    shutil.copyfile(args.unsigned_msi, unsigned_msi)
    if digest(unsigned_msi) != input_hash:
        raise SigningError("Unsigned MSI changed while copying")
    host("unsigned", path=str(unsigned_msi))
    # Check the exact installed file set before paying to sign the installer.
    msi_scratch = scratch / "msi-check"
    msi_scratch.mkdir()
    host("msi", path=str(unsigned_msi), payload=str(payload), scratch=str(msi_scratch))
    if digest(unsigned_msi) != input_hash:
        raise SigningError("Unsigned MSI changed during its content check")
    signed = budget.sign_once(MSI, unsigned_msi, sign, verify)
    host("msi", path=str(signed), payload=str(payload), scratch=str(msi_scratch))
    staging = scratch / "assets"
    staging.mkdir()
    shutil.copyfile(archive, staging / ARCHIVE)
    checksum(staging / ARCHIVE)
    shutil.copyfile(signed, staging / MSI)
    checksum(staging / MSI)
    return staging


def run(args):
    if os.name != "nt" or os.environ.get("GITHUB_ACTIONS") == "true" or os.environ.get("CI") == "true":
        raise SigningError("This paid signer runs only on an owner-approved local Windows host")
    if (not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", args.tag)
            or args.confirm_sign != args.tag or args.confirm_paid_signatures != 4):
        raise SigningError("Signing requires the exact tag and explicit approval for up to four paid signatures")
    if not sys.stdin.isatty():
        raise SigningError("Use an interactive local terminal for signing")
    repositories = [checked_path(REPO, directory=True), checked_path(args.agent_repo, directory=True)]
    candidate = {"schema_version": 1, "tag": args.tag,
                 "wrapper_commit": clean_source(repositories[0], args.tag),
                 "agent_commit": clean_source(repositories[1], args.tag)}
    args.output_directory = checked_path(external(args.output_directory, repositories), directory=True)
    # A fixed per-candidate location prevents an ordinary rerun from choosing
    # a fresh ledger and paying for the same source again.
    state_root = external(Path(os.environ["LOCALAPPDATA"]) / "LabTether" / "ReleaseSigning", repositories)
    state_root.mkdir(parents=True, exist_ok=True)
    checked_path(state_root, directory=True)
    host("protect", path=str(state_root))
    state = state_root / f"{args.tag}-{candidate['wrapper_commit']}-{candidate['agent_commit']}"
    if args.unsigned_archive:
        state.mkdir(exist_ok=True)
    state = checked_path(state, directory=True)
    if (any(args.output_directory.iterdir()) or state == args.output_directory
            or state in args.output_directory.parents or args.output_directory in state.parents):
        raise SigningError("State and empty output directories must be separate")
    if args.unsigned_archive:
        if args.signed_assets or args.unsigned_msi:
            raise SigningError("Sign the ZIP and MSI in separate invocations")
        args.unsigned_archive = checked_path(external(args.unsigned_archive, repositories))
        if args.unsigned_archive.name != UNSIGNED_ARCHIVE:
            raise SigningError("Unexpected unsigned archive name")
        candidate["unsigned_archive_sha256"] = digest(args.unsigned_archive)
    else:
        if not args.signed_assets or not args.unsigned_msi:
            raise SigningError("The MSI stage requires the signed ZIP assets and unsigned MSI")
        args.signed_assets = checked_path(external(args.signed_assets, repositories), directory=True)
        args.unsigned_msi = checked_path(external(args.unsigned_msi, repositories))
        if args.unsigned_msi.name != UNSIGNED_MSI:
            raise SigningError("Unexpected unsigned MSI name")
        candidate["unsigned_archive_sha256"] = read_json(state / "candidate.json")["unsigned_archive_sha256"]
    thumbprint = getpass.getpass("Local CKA certificate thumbprint (hidden): ").strip().upper()
    if not re.fullmatch(r"[0-9A-F]{40}", thumbprint):
        raise SigningError("A certificate thumbprint is required")
    host("certificate", thumbprint=thumbprint)
    candidate["certificate_sha256"] = hashlib.sha256(thumbprint.encode("ascii")).hexdigest()
    host("protect", path=str(state))
    host("protect", path=str(args.output_directory))

    def verify(path):
        host("verify", path=str(path), thumbprint=thumbprint)

    def sign(path):
        host("sign", path=str(path), thumbprint=thumbprint, tag=args.tag,
             confirm_sign=args.confirm_sign, confirm_paid_signatures=args.confirm_paid_signatures)

    with SigningBudget(state, candidate, create=bool(args.unsigned_archive)).locked() as budget:
        with tempfile.TemporaryDirectory(prefix="labtether-esigner-") as temporary:
            scratch = checked_path(external(temporary, repositories), directory=True)
            host("protect", path=str(scratch))
            staging = (archive_stage if args.unsigned_archive else msi_stage)(
                args, candidate, budget, scratch, sign, verify)
            for repository, key in zip(repositories, ("wrapper_commit", "agent_commit")):
                if clean_source(repository, args.tag) != candidate[key]:
                    raise SigningError("Release source changed during signing")
            place_assets(staging, args.output_directory)
    print("Signed assets prepared locally. Nothing was published.")
    print("Next: run verify-signed-windows-release.ps1 on these exact assets.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("tag", "agent-repo", "output-directory", "confirm-sign"):
        parser.add_argument("--" + option, required=True)
    parser.add_argument("--confirm-paid-signatures", type=int, required=True)
    parser.add_argument("--unsigned-archive", type=Path)
    parser.add_argument("--signed-assets", type=Path)
    parser.add_argument("--unsigned-msi", type=Path)
    try:
        run(parser.parse_args())
    except SigningError as error:
        print(str(error), file=sys.stderr)
        return 1
    except (OSError, ValueError, KeyError, TypeError, ArchivePolicyError, subprocess.SubprocessError):
        # Do not expose native output, paths or certificate selectors in errors.
        print("Local signing stopped. Keep its saved state for inspection; do not retry paid signatures.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
