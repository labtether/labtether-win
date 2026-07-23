#!/usr/bin/env python3
"""Malicious fixtures for the Mac-side Windows release archive gate."""

from __future__ import annotations

import stat
import tempfile
import warnings
import zipfile
from pathlib import Path

from safe_extract_windows_release import ArchivePolicyError, extract_archive, validate_archive


def write_archive(path: Path, entries: list[tuple[zipfile.ZipInfo | str, bytes]]) -> None:
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", UserWarning)
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, content in entries:
                archive.writestr(name, content)


def expect_rejection(
    label: str,
    entries: list[tuple[zipfile.ZipInfo | str, bytes]],
    expected_raw_name: str | None = None,
) -> None:
    with tempfile.TemporaryDirectory(prefix="labtether-archive-policy-") as temporary:
        archive_path = Path(temporary, "fixture.zip")
        write_archive(archive_path, entries)
        if expected_raw_name is not None:
            with zipfile.ZipFile(archive_path, "r") as archive:
                names = [entry.filename for entry in archive.infolist()]
            if expected_raw_name not in names:
                normalized_name = expected_raw_name.replace("\\", "/")
                raw = archive_path.read_bytes()
                patched = raw.replace(normalized_name.encode(), expected_raw_name.encode())
                if patched == raw:
                    raise AssertionError(f"archive fixture lost its raw path: {label}")
                archive_path.write_bytes(patched)
                with zipfile.ZipFile(archive_path, "r") as archive:
                    names = [entry.filename for entry in archive.infolist()]
                if expected_raw_name not in names:
                    raise AssertionError(f"archive fixture lost its raw path: {label}")
        try:
            validate_archive(archive_path)
        except ArchivePolicyError:
            return
        raise AssertionError(f"malicious archive fixture was accepted: {label}")


def typed_entry(name: str, file_type: int, content: bytes = b"x") -> zipfile.ZipInfo:
    entry = zipfile.ZipInfo(name)
    entry.create_system = 3
    entry.external_attr = (file_type | 0o755) << 16
    entry.compress_type = zipfile.ZIP_DEFLATED
    return entry


def raw_name_entry(name: str) -> zipfile.ZipInfo:
    entry = zipfile.ZipInfo("placeholder")
    # ZipInfo normalizes platform path separators in its constructor on
    # Windows. Assign both fields afterward so the serialized fixture retains
    # the attacker-controlled name on every host.
    entry.filename = name
    entry.orig_filename = name
    entry.compress_type = zipfile.ZIP_DEFLATED
    return entry


def main() -> int:
    malicious = {
        "parent traversal": [("../escape", b"x")],
        "absolute path": [("/absolute", b"x")],
        "drive-letter path": [("C:/drive", b"x")],
        "leading dot segment": [("./leading", b"x")],
        "interior dot segment": [("a/./b", b"x")],
        "repeated separator": [("a//b", b"x")],
        "backslash confusion": [(raw_name_entry("a\\b"), b"x")],
        "exact duplicate": [("same", b"1"), ("same", b"2")],
        "case-fold collision": [("Payload.dll", b"1"), ("payload.dll", b"2")],
        "Unicode normalization collision": [("café", b"1"), ("café", b"2")],
        "file and descendant": [("Assets", b"1"), ("Assets/agent.exe", b"2")],
        "Windows reserved name": [("Assets/CON.txt", b"x")],
        "Windows trailing dot": [("Assets/name.", b"x")],
        "symbolic link": [(typed_entry("link", stat.S_IFLNK), b"target")],
        "special FIFO": [(typed_entry("pipe", stat.S_IFIFO), b"")],
        "directory type mismatch": [(typed_entry("folder/", stat.S_IFREG), b"")],
    }
    for label, entries in malicious.items():
        expected_raw_name = "a\\b" if label == "backslash confusion" else None
        expect_rejection(label, entries, expected_raw_name)

    with tempfile.TemporaryDirectory(prefix="labtether-archive-bounds-") as temporary:
        archive_path = Path(temporary, "fixture.zip")
        write_archive(archive_path, [("one", b"11"), ("two", b"22"), ("three", b"33")])
        for label, bounds in {
            "entry count": {"maximum_entries": 2},
            "per-entry bytes": {"maximum_entry_bytes": 1},
            "total bytes": {"maximum_uncompressed_bytes": 5},
        }.items():
            try:
                validate_archive(archive_path, **bounds)
            except (ArchivePolicyError, ValueError):
                continue
            raise AssertionError(f"archive bound fixture was accepted: {label}")

    with tempfile.TemporaryDirectory(prefix="labtether-archive-good-") as temporary:
        root = Path(temporary)
        archive_path = root / "fixture.zip"
        destination = root / "extracted"
        destination.mkdir(mode=0o700)
        write_archive(
            archive_path,
            [
                (typed_entry("Assets/", stat.S_IFDIR), b""),
                (typed_entry("Assets/labtether-agent.exe", stat.S_IFREG), b"agent"),
                (typed_entry("LabTetherAgent.exe", stat.S_IFREG), b"wrapper"),
            ],
        )
        extract_archive(archive_path, destination)
        if (destination / "Assets" / "labtether-agent.exe").read_bytes() != b"agent":
            raise AssertionError("valid archive did not re-extract exact bytes")
        if (destination / "LabTetherAgent.exe").read_bytes() != b"wrapper":
            raise AssertionError("valid archive did not re-extract exact bytes")

    print("safe archive malicious-fixture tests passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
