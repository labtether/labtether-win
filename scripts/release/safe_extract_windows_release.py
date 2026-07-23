#!/usr/bin/env python3
"""Validate and extract the Windows release ZIP without path normalization risks."""

from __future__ import annotations

import argparse
import os
import re
import stat
import unicodedata
import zipfile
from dataclasses import dataclass
from pathlib import Path


DEFAULT_MAXIMUM_ENTRIES = 2048
DEFAULT_MAXIMUM_UNCOMPRESSED_BYTES = 512 * 1024 * 1024
DEFAULT_MAXIMUM_ENTRY_BYTES = 256 * 1024 * 1024
ALLOWED_COMPRESSION_METHODS = {zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED}
WINDOWS_INVALID_CHARACTERS = re.compile(r'[<>:"|?*\x00-\x1f]')
WINDOWS_RESERVED_BASENAMES = {
    "con",
    "prn",
    "aux",
    "nul",
    *(f"com{number}" for number in range(1, 10)),
    *(f"lpt{number}" for number in range(1, 10)),
}


class ArchivePolicyError(RuntimeError):
    """Raised when an archive is unsafe or outside the release contract."""


@dataclass(frozen=True)
class ValidatedEntry:
    info: zipfile.ZipInfo
    relative_path: str
    is_directory: bool
    collision_key: str


def _validate_entry_name(name: str, is_directory: bool) -> tuple[str, str]:
    if not name or name.isspace() or len(name.encode("utf-8")) > 4096:
        raise ArchivePolicyError("archive contains an empty or oversized path")
    if "\\" in name or name.startswith("/") or re.match(r"^[A-Za-z]:", name):
        raise ArchivePolicyError("archive contains an absolute, drive-letter, or backslash path")
    if is_directory:
        if not name.endswith("/"):
            raise ArchivePolicyError("archive directory type conflicts with its path")
        relative_path = name[:-1]
    else:
        if name.endswith("/"):
            raise ArchivePolicyError("archive file type conflicts with its path")
        relative_path = name
    segments = relative_path.split("/")
    if not relative_path or any(segment in {"", ".", ".."} for segment in segments):
        raise ArchivePolicyError("archive contains an empty, dot, or traversal path segment")
    for segment in segments:
        if len(segment.encode("utf-8")) > 255:
            raise ArchivePolicyError("archive contains an oversized path segment")
        if WINDOWS_INVALID_CHARACTERS.search(segment) or segment.endswith((" ", ".")):
            raise ArchivePolicyError("archive contains a path Windows would normalize or reject")
        basename = segment.split(".", 1)[0].casefold()
        if basename in WINDOWS_RESERVED_BASENAMES:
            raise ArchivePolicyError("archive contains a reserved Windows path segment")
    collision_key = unicodedata.normalize("NFC", relative_path).casefold()
    return relative_path, collision_key


def validate_archive(
    archive_path: Path,
    *,
    maximum_entries: int = DEFAULT_MAXIMUM_ENTRIES,
    maximum_uncompressed_bytes: int = DEFAULT_MAXIMUM_UNCOMPRESSED_BYTES,
    maximum_entry_bytes: int = DEFAULT_MAXIMUM_ENTRY_BYTES,
) -> list[ValidatedEntry]:
    if maximum_entries <= 0 or maximum_uncompressed_bytes <= 0 or maximum_entry_bytes <= 0:
        raise ValueError("archive bounds must be positive")
    archive_stat = archive_path.lstat()
    if not stat.S_ISREG(archive_stat.st_mode) or archive_path.is_symlink():
        raise ArchivePolicyError("release archive must be a regular non-symlink file")

    try:
        archive = zipfile.ZipFile(archive_path, "r")
    except (OSError, zipfile.BadZipFile) as error:
        raise ArchivePolicyError("release archive is not a readable ZIP") from error

    with archive:
        infos = archive.infolist()
        if not infos or len(infos) > maximum_entries:
            raise ArchivePolicyError("archive entry count is outside the release limit")
        entries: list[ValidatedEntry] = []
        names: set[str] = set()
        collision_keys: set[str] = set()
        total_uncompressed = 0
        for info in infos:
            if info.flag_bits & 0x1:
                raise ArchivePolicyError("encrypted archive entries are forbidden")
            if info.compress_type not in ALLOWED_COMPRESSION_METHODS:
                raise ArchivePolicyError("archive uses an unsupported compression method")
            if info.file_size < 0 or info.file_size > maximum_entry_bytes:
                raise ArchivePolicyError("archive entry exceeds the uncompressed size limit")
            if total_uncompressed > maximum_uncompressed_bytes - info.file_size:
                raise ArchivePolicyError("archive exceeds the total uncompressed size limit")
            total_uncompressed += info.file_size

            unix_mode = (info.external_attr >> 16) & 0xFFFF
            unix_type = stat.S_IFMT(unix_mode)
            if unix_type not in {0, stat.S_IFREG, stat.S_IFDIR}:
                raise ArchivePolicyError("archive contains a symlink or special entry")
            if info.external_attr & 0x400:
                raise ArchivePolicyError("archive contains a reparse-point entry")
            is_directory = info.is_dir()
            if (unix_type == stat.S_IFDIR) != is_directory and unix_type != 0:
                raise ArchivePolicyError("archive entry type conflicts with its path")
            if is_directory and info.file_size != 0:
                raise ArchivePolicyError("archive directory entry has file content")

            relative_path, collision_key = _validate_entry_name(info.filename, is_directory)
            if info.filename in names or collision_key in collision_keys:
                raise ArchivePolicyError("archive contains duplicate or normalized-colliding paths")
            names.add(info.filename)
            collision_keys.add(collision_key)
            entries.append(ValidatedEntry(info, relative_path, is_directory, collision_key))

        nodes = {entry.collision_key: entry.is_directory for entry in entries}
        for entry in entries:
            components = entry.collision_key.split("/")
            for index in range(1, len(components)):
                parent_key = "/".join(components[:index])
                if parent_key in nodes and not nodes[parent_key]:
                    raise ArchivePolicyError("archive maps a file and descendant to the same path")
        return entries


def _ensure_directory(root: Path, relative_path: str) -> Path:
    current = root
    for segment in relative_path.split("/") if relative_path else []:
        current = current / segment
        try:
            current.mkdir(mode=0o700)
        except FileExistsError:
            pass
        item_stat = current.lstat()
        if not stat.S_ISDIR(item_stat.st_mode) or current.is_symlink():
            raise ArchivePolicyError("archive extraction encountered a non-directory parent")
    return current


def extract_archive(
    archive_path: Path,
    destination: Path,
    *,
    maximum_entries: int = DEFAULT_MAXIMUM_ENTRIES,
    maximum_uncompressed_bytes: int = DEFAULT_MAXIMUM_UNCOMPRESSED_BYTES,
    maximum_entry_bytes: int = DEFAULT_MAXIMUM_ENTRY_BYTES,
) -> None:
    destination_stat = destination.lstat()
    if not stat.S_ISDIR(destination_stat.st_mode) or destination.is_symlink():
        raise ArchivePolicyError("release extraction destination must be a real directory")
    if any(destination.iterdir()):
        raise ArchivePolicyError("release extraction destination must be empty")
    root = destination.resolve(strict=True)
    entries = validate_archive(
        archive_path,
        maximum_entries=maximum_entries,
        maximum_uncompressed_bytes=maximum_uncompressed_bytes,
        maximum_entry_bytes=maximum_entry_bytes,
    )
    nofollow = getattr(os, "O_NOFOLLOW", 0)
    with zipfile.ZipFile(archive_path, "r") as archive:
        for entry in entries:
            if entry.is_directory:
                _ensure_directory(root, entry.relative_path)
                continue
            parent = _ensure_directory(root, entry.relative_path.rpartition("/")[0])
            destination_path = parent / entry.relative_path.rsplit("/", 1)[-1]
            flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | nofollow
            try:
                descriptor = os.open(destination_path, flags, 0o600)
            except OSError as error:
                raise ArchivePolicyError("release extraction refused an existing or unsafe file") from error
            written = 0
            try:
                with archive.open(entry.info, "r") as source, os.fdopen(descriptor, "wb") as target:
                    descriptor = -1
                    while True:
                        chunk = source.read(1024 * 1024)
                        if not chunk:
                            break
                        written += len(chunk)
                        if written > entry.info.file_size:
                            raise ArchivePolicyError("archive entry expanded beyond its declared size")
                        target.write(chunk)
            except (OSError, zipfile.BadZipFile) as error:
                raise ArchivePolicyError("archive entry failed integrity validation") from error
            finally:
                if descriptor >= 0:
                    os.close(descriptor)
            if written != entry.info.file_size:
                raise ArchivePolicyError("archive entry size differs from its central-directory record")

    for current_root, directories, files in os.walk(root, followlinks=False):
        for name in [*directories, *files]:
            item = Path(current_root, name)
            item_stat = item.lstat()
            if not (stat.S_ISDIR(item_stat.st_mode) or stat.S_ISREG(item_stat.st_mode)) or item.is_symlink():
                raise ArchivePolicyError("extracted release contains a symlink or special file")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument("destination", type=Path)
    arguments = parser.parse_args()
    try:
        extract_archive(arguments.archive, arguments.destination)
    except (ArchivePolicyError, OSError) as error:
        parser.exit(1, f"error: {error}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
