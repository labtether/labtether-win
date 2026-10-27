"""Durable, once-per-file reservations for the four paid Windows signatures."""

from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat


AUTHORED = ("LabTetherAgent.exe", "LabTetherAgent.dll", "Assets/labtether-agent.exe")
MSI = "labtether-agent-win-x64.msi"
PLAN = (*AUTHORED, MSI)


class SigningError(RuntimeError):
    pass


def checked_path(path, *, directory=False):
    """Reject links and Windows junctions before resolving a local path."""
    path = Path(os.path.abspath(path))
    for item in (path, *path.parents):
        info = item.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise SigningError("Signing paths must not contain links or reparse points")
    info = path.stat()
    if not (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)):
        raise SigningError("A signing path has the wrong file type")
    return path


def digest(path):
    path = checked_path(path)
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def read_json(path):
    path = checked_path(path)
    if path.stat().st_size > 1024 * 1024:
        raise SigningError("Signing state exceeds its size limit")
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, UnicodeError) as error:
        raise SigningError("Signing state is invalid; manual inspection is required") from error


def write_new_json(path, value):
    with Path(path).open("x", encoding="utf-8") as target:
        json.dump(value, target, sort_keys=True)
        target.write("\n")
        target.flush()
        os.fsync(target.fileno())


class SigningBudget:
    """The caller holds the candidate lock through signing and output placement.

    Pending reservations survive failures. Only completed outputs can be reused;
    there is deliberately no reset, retry, or force option.
    """

    def __init__(self, root, candidate, *, create=False):
        self.root = checked_path(root, directory=True)
        self.candidate = candidate
        self.create = create

    @contextmanager
    def locked(self):
        lock = self.root / "operation.lock"
        try:
            handle = lock.open("x")
        except FileExistsError as error:
            raise SigningError("Another signing operation or an interrupted lock needs inspection") from error
        try:
            marker = self.root / "candidate.json"
            if marker.exists():
                if read_json(marker) != self.candidate:
                    raise SigningError("Signing state belongs to different source, input, or certificate")
            elif self.create and set(self.root.iterdir()) == {lock}:
                write_new_json(marker, self.candidate)
            else:
                raise SigningError("Signing state is missing; refusing to reset the paid budget")
            yield self
        finally:
            identity = os.fstat(handle.fileno())
            handle.close()
            if lock.exists() and os.path.samestat(identity, lock.stat()):
                lock.unlink()

    def paths(self, name):
        if name not in PLAN:
            raise SigningError("File is outside the four-signature plan")
        slot = PLAN.index(name) + 1
        return self.root / f"signature-{slot}.json", self.root / f"signed-{slot}{Path(name).suffix}"

    def completed(self, name, verify):
        record_path, signed = self.paths(name)
        record = read_json(record_path)
        if (record.get("name") != name or record.get("status") != "complete"
                or record.get("signed_sha256") != digest(signed)):
            raise SigningError("An earlier signature is incomplete or changed; signing again is refused")
        verify(signed)
        return signed, record

    def sign_once(self, name, source, sign, verify):
        record_path, signed = self.paths(name)
        source = checked_path(source)
        source_hash = digest(source)
        if record_path.exists():
            cached, record = self.completed(name, verify)
            if record.get("input_sha256") != source_hash:
                raise SigningError("Signing input changed after its first reservation")
            return cached
        if signed.exists():
            raise SigningError("Signed output has no reservation; refusing to reset the paid budget")
        if name == MSI:
            for payload in AUTHORED:
                self.completed(payload, verify)
        record = {"name": name, "status": "reserved", "input_sha256": source_hash}
        # Reserve and flush before a signer can incur a charge. A timeout,
        # failed verification or process exit leaves this reservation intact.
        write_new_json(record_path, record)
        with source.open("rb") as original, signed.open("xb") as target:
            shutil.copyfileobj(original, target)
        if digest(signed) != source_hash:
            raise SigningError("Signing input changed while copying")
        sign(signed)
        verify(signed)
        record.update(status="complete", signed_sha256=digest(signed))
        completed_path = self.root / f"{record_path.name}.complete"
        write_new_json(completed_path, record)
        os.replace(completed_path, record_path)
        return signed
