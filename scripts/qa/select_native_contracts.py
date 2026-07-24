#!/usr/bin/env python3
"""Select the Windows QA contracts required by a set of changed paths."""

from __future__ import annotations

import argparse
import subprocess
from pathlib import Path


def _matches(path: str, prefixes: tuple[str, ...], exact: tuple[str, ...] = ()) -> bool:
    return path in exact or path.startswith(prefixes)


def classify(paths: list[str]) -> dict[str, object]:
    normalized_paths = []
    for raw_path in paths:
        path = raw_path.replace("\\", "/")
        while path.startswith("./"):
            path = path[2:]
        if path:
            normalized_paths.append(path)
    normalized = sorted(set(normalized_paths))
    fail_safe = "__all__" in normalized

    connection = fail_safe or any(
        _matches(
            path,
            (
                "src/LabTetherAgent/Api/",
                "src/LabTetherAgent/Process/AgentProcess",
                "src/LabTetherAgent/Services/AgentSetup",
                "src/LabTetherAgent/Services/ConnectionTester",
                "src/LabTetherAgent/Settings/AgentEnvironmentBuilder",
                "src/LabTetherAgent/Settings/AgentSettings",
                "src/LabTetherAgent/Settings/SettingsValidator",
            ),
        )
        for path in normalized
    )
    permissions = fail_safe or any(
        _matches(
            path,
            (
                "src/LabTetherAgent/App/LoginItemManager",
                "src/LabTetherAgent/Settings/CredentialStore",
                "src/LabTetherAgent/Settings/SecureFile",
            ),
            (
                "src/LabTetherAgent/Package.appxmanifest",
                "src/LabTetherAgent/app.manifest",
            ),
        )
        for path in normalized
    )
    packaging = fail_safe or any(
        _matches(
            path,
            (
                ".github/workflows/",
                "scripts/build-",
                "scripts/download-agent",
                "scripts/release/",
                "src/LabTetherAgent/Assets/",
            ),
            (
                "global.json",
                "LabTetherAgent.sln",
                "src/LabTetherAgent/LabTetherAgent.csproj",
                "src/LabTetherAgent/Package.appxmanifest",
                "src/LabTetherAgent/app.manifest",
            ),
        )
        for path in normalized
    )
    signing = fail_safe or any(
        path.startswith("scripts/release/")
        or path == ".github/workflows/release.yml"
        or "sign" in Path(path).name.lower()
        for path in normalized
    )
    platform = fail_safe or any(path.startswith("src/LabTetherAgent/") for path in normalized)

    selected: list[str] = []
    for enabled, name in (
        (connection, "hub-connection-security"),
        (permissions, "credential-permissions"),
        (packaging, "packaging-runtime"),
        (signing, "signing-boundary"),
        (platform, "native-windows-ui"),
    ):
        if enabled:
            selected.append(name)

    installed = bool(selected)
    return {
        "connection": connection,
        "permissions": permissions,
        "packaging": packaging,
        "signing": signing,
        "platform": platform,
        "installed": installed,
        "native_host": installed,
        "contracts": ",".join(selected) if selected else "source-only",
        "paths": normalized,
    }


def changed_paths(base: str, head: str) -> list[str]:
    if not base or set(base) == {"0"}:
        command = ["git", "diff-tree", "--root", "--no-commit-id", "--name-only", "-r", head]
    else:
        command = ["git", "diff", "--name-only", f"{base}...{head}"]
    try:
        output = subprocess.check_output(command, text=True, stderr=subprocess.DEVNULL)
    except (subprocess.CalledProcessError, FileNotFoundError):
        # Missing history must broaden QA, never silently skip a native contract.
        return ["__all__"]
    return output.splitlines()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="")
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--github-output")
    parser.add_argument("--path", action="append", dest="paths")
    parser.add_argument("--full", action="store_true")
    args = parser.parse_args()

    selected_paths = (
        ["__all__"]
        if args.full
        else args.paths if args.paths is not None else changed_paths(args.base, args.head)
    )
    result = classify(selected_paths)
    print(f"Windows QA contracts: {result['contracts']}")
    print(f"Changed paths evaluated: {len(result['paths'])}")

    if args.github_output:
        output_path = Path(args.github_output)
        with output_path.open("a", encoding="utf-8") as output:
            for key in (
                "connection",
                "permissions",
                "packaging",
                "signing",
                "platform",
                "installed",
                "native_host",
                "contracts",
            ):
                value = result[key]
                output.write(f"{key}={str(value).lower() if isinstance(value, bool) else value}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
