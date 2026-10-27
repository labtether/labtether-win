"""Protect against duplicate paid calls and signing the wrong prepared bytes.

The filesystem and release orchestration are real. Only Windows OS signature
and MSI operations are stubbed; these tests never contact a signing provider.
"""

import json
from pathlib import Path
from types import SimpleNamespace
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import sign_windows_esigner as release
from windows_signing_budget import AUTHORED, MSI, SigningBudget, SigningError, digest, read_json


class SigningTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="labtether-signing-test-")
        self.root = Path(self.temporary.name).resolve()
        self.state = self.root / "state"
        self.state.mkdir()
        self.candidate = {
            "schema_version": 1, "tag": "v1.2.3", "wrapper_commit": "a" * 40,
            "agent_commit": "b" * 40, "unsigned_archive_sha256": "c" * 64,
            "certificate_sha256": "d" * 64,
        }
        self.calls = []

    def tearDown(self):
        self.temporary.cleanup()

    def source(self, name="source.exe"):
        path = self.root / name
        path.write_bytes(b"unsigned fixture")
        return path

    def sign(self, path):
        self.calls.append(path.name)
        path.write_bytes(path.read_bytes() + b"\nsynthetic signature")

    def verify(self, path):
        if not path.read_bytes().endswith(b"\nsynthetic signature"):
            raise SigningError("Synthetic OS signature check failed")

    def budget(self, create=True):
        return SigningBudget(self.state, self.candidate, create=create)

    def test_four_file_plan_reuses_bytes_and_rejects_unknown_or_changed_inputs(self):
        source = self.source()
        with self.budget().locked() as budget:
            for name in (*AUTHORED, MSI):
                first = budget.sign_once(name, source, self.sign, self.verify)
                self.assertEqual(first, budget.sign_once(name, source, self.sign, self.verify))
            self.assertEqual(4, len(self.calls))
            with self.assertRaises(SigningError):
                budget.sign_once("vendor.dll", source, self.sign, self.verify)
            source.write_bytes(b"changed unsigned fixture")
            with self.assertRaises(SigningError):
                budget.sign_once(AUTHORED[0], source, self.sign, self.verify)
            self.assertEqual(4, len(self.calls))

    def test_failed_or_interrupted_signature_is_never_paid_twice(self):
        source = self.source()
        def failed_signer(path):
            self.sign(path)
            raise SigningError("Synthetic timeout after provider accepted the request")
        with self.budget().locked() as budget:
            with self.assertRaises(SigningError):
                budget.sign_once(AUTHORED[0], source, failed_signer, self.verify)
        with self.budget(create=False).locked() as budget:
            with self.assertRaises(SigningError):
                budget.sign_once(AUTHORED[0], source, self.sign, self.verify)
        self.assertEqual(1, len(self.calls))
        self.assertEqual("reserved", read_json(self.state / "signature-1.json")["status"])

    def test_candidate_lock_and_identity_prevent_parallel_or_different_signing(self):
        with self.budget().locked():
            with self.assertRaises(SigningError):
                with self.budget().locked():
                    self.fail("Two processes acquired the same signing state")
        changed = {**self.candidate, "certificate_sha256": "e" * 64}
        with self.assertRaises(SigningError):
            with SigningBudget(self.state, changed).locked():
                self.fail("Another certificate reused the candidate")

    def test_missing_state_or_tampered_cached_bytes_never_restarts_signing(self):
        source = self.source()
        with self.budget().locked() as budget:
            cached = budget.sign_once(AUTHORED[0], source, self.sign, self.verify)
            cached.write_bytes(b"altered")
            with self.assertRaises(SigningError):
                budget.sign_once(AUTHORED[0], source, self.sign, self.verify)
        (self.state / "candidate.json").unlink()
        with self.assertRaises(SigningError):
            with self.budget().locked():
                self.fail("Missing state reset a partly used budget")
        self.assertEqual(1, len(self.calls))

    def test_msi_cannot_spend_before_all_payload_signatures_complete(self):
        with self.budget().locked() as budget:
            with self.assertRaises((SigningError, OSError)):
                budget.sign_once(MSI, self.source(), self.sign, self.verify)
        self.assertEqual([], self.calls)

    def archive(self, *, tamper=False):
        payload = self.root / "prepared"
        payload.mkdir()
        for name in (*AUTHORED, "vendor.dll", "AGENT_VERSION"):
            path = payload / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.candidate["tag"].encode() if name == "AGENT_VERSION" else b"unsigned fixture")
        records = [{"path": name, "size": (payload / name).stat().st_size, "sha256": digest(payload / name)}
                   for name in (*AUTHORED, "vendor.dll", "AGENT_VERSION")]
        proof = {key: self.candidate[key] for key in ("tag", "wrapper_commit", "agent_commit")}
        proof.update(schema_version=1, payload="labtether-agent-win-x64-unsigned",
                     published_files=records, published_file_count=len(records),
                     published_bytes=sum(item["size"] for item in records),
                     authored_payloads=[item for item in records if item["path"] in AUTHORED])
        (payload / release.PROVENANCE).write_text(json.dumps(proof), encoding="utf-8")
        if tamper:
            (payload / AUTHORED[0]).write_bytes(b"not the prepared app")
        archive = self.root / release.UNSIGNED_ARCHIVE
        with zipfile.ZipFile(archive, "w") as target:
            for path in payload.rglob("*"):
                if path.is_file():
                    target.write(path, path.relative_to(payload).as_posix())
        self.candidate["unsigned_archive_sha256"] = digest(archive)
        return archive

    def test_two_stages_keep_release_contract_and_reuse_exact_signed_files(self):
        archive = self.archive()
        unsigned_msi = self.source(release.UNSIGNED_MSI)
        args = SimpleNamespace(unsigned_archive=archive, unsigned_msi=unsigned_msi)
        with patch.object(release, "host") as os_operation, self.budget().locked() as budget:
            scratch = self.root / "zip-stage"
            scratch.mkdir()
            assets = release.archive_stage(args, self.candidate, budget, scratch, self.sign, self.verify)
            self.assertEqual({release.ARCHIVE, release.ARCHIVE + ".sha256"}, {p.name for p in assets.iterdir()})
            args.signed_assets = assets
            for attempt in range(2):
                scratch = self.root / f"msi-stage-{attempt}"
                scratch.mkdir()
                complete = release.msi_stage(args, self.candidate, budget, scratch, self.sign, self.verify)
                self.assertEqual({release.ARCHIVE, release.ARCHIVE + ".sha256", MSI, MSI + ".sha256"},
                                 {p.name for p in complete.iterdir()})
                self.assertEqual(digest(assets / release.ARCHIVE), digest(complete / release.ARCHIVE))
            self.assertEqual(4, len(self.calls))
            self.assertEqual(4, sum(call.args[0] == "msi" for call in os_operation.call_args_list))

    def test_provenance_tampering_is_rejected_before_any_paid_call(self):
        args = SimpleNamespace(unsigned_archive=self.archive(tamper=True))
        scratch = self.root / "scratch"
        scratch.mkdir()
        with patch.object(release, "host"), self.budget().locked() as budget:
            with self.assertRaises(SigningError):
                release.archive_stage(args, self.candidate, budget, scratch, self.sign, self.verify)
        self.assertEqual([], self.calls)

    def test_msi_mismatch_is_rejected_before_fourth_paid_call(self):
        args = SimpleNamespace(unsigned_archive=self.archive(), unsigned_msi=self.source(release.UNSIGNED_MSI))
        with patch.object(release, "host"), self.budget().locked() as budget:
            scratch = self.root / "zip"
            scratch.mkdir()
            args.signed_assets = release.archive_stage(args, self.candidate, budget, scratch, self.sign, self.verify)
            scratch = self.root / "msi"
            scratch.mkdir()
            def reject_msi(action, **_values):
                if action == "msi":
                    raise SigningError("Synthetic MSI payload mismatch")
            with patch.object(release, "host", side_effect=reject_msi), self.assertRaises(SigningError):
                release.msi_stage(args, self.candidate, budget, scratch, self.sign, self.verify)
        self.assertEqual(3, len(self.calls))

    def test_regenerated_msi_cannot_replace_the_checked_bytes(self):
        args = SimpleNamespace(unsigned_archive=self.archive(), unsigned_msi=self.source(release.UNSIGNED_MSI))
        with patch.object(release, "host"), self.budget().locked() as budget:
            scratch = self.root / "zip"
            scratch.mkdir()
            args.signed_assets = release.archive_stage(args, self.candidate, budget, scratch, self.sign, self.verify)
            scratch = self.root / "msi"
            scratch.mkdir()
            def replace_external_msi(action, **values):
                if action == "msi" and Path(values["path"]).name == release.UNSIGNED_MSI:
                    self.assertNotEqual(args.unsigned_msi, Path(values["path"]))
                    args.unsigned_msi.write_bytes(b"regenerated wrong payload")
            with patch.object(release, "host", side_effect=replace_external_msi):
                assets = release.msi_stage(args, self.candidate, budget, scratch, self.sign, self.verify)
            self.assertEqual(b"unsigned fixture\nsynthetic signature", (assets / MSI).read_bytes())
        self.assertEqual(4, len(self.calls))

    def test_native_failure_does_not_leak_private_output(self):
        result = subprocess.CompletedProcess([], 1, "synthetic-private-selector", "synthetic-private-error")
        with patch.dict("os.environ", {"SystemRoot": str(self.root)}), patch.object(release.subprocess, "run", return_value=result):
            with self.assertRaises(SigningError) as error:
                release.host("certificate", thumbprint="synthetic-private-selector")
        self.assertNotIn("synthetic-private", str(error.exception))

    def test_hosted_environment_cannot_reach_certificate_or_signer(self):
        with patch.dict("os.environ", {"GITHUB_ACTIONS": "true"}), patch.object(release, "host") as os_operation:
            with self.assertRaises(SigningError):
                release.run(SimpleNamespace())
            os_operation.assert_not_called()


if __name__ == "__main__":
    unittest.main()
