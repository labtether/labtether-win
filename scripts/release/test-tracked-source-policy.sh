#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/release/windows-release-common.sh
source "$SCRIPT_DIR/windows-release-common.sh"

require_command git

fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/labtether-source-policy.XXXXXX")"
cleanup() {
  rm -rf -- "$fixture_root"
}
trap cleanup EXIT

repo="$fixture_root/source"
mkdir -p -- "$repo/scripts/release"
cp -- "$SCRIPT_DIR/windows-release-common.sh" "$repo/scripts/release/windows-release-common.sh"
cp -- "$SCRIPT_DIR/windows-release-policy.ps1" "$repo/scripts/release/windows-release-policy.ps1"
printf 'ordinary tracked source\n' > "$repo/ordinary.txt"
printf 'NUL-safe filename fixture\n' > "$repo/line"$'\n'"break.txt"
git -C "$repo" init -q
git -C "$repo" config user.name "Release Policy Fixture"
git -C "$repo" config user.email "release-policy@example.invalid"
git -C "$repo" add -- .
git -C "$repo" commit -qm "clean source fixture"
git -C "$repo" tag v1.2.3

# The scanner source itself and a newline-bearing tracked filename must pass.
assert_tracked_source_policy "$repo"

commit="$(git -C "$repo" rev-parse HEAD)"
git -C "$repo" update-index --add --cacheinfo "160000,$commit,nested-repository"
if (assert_tracked_source_policy "$repo") 2>/dev/null; then
  die "tracked source policy accepted a non-regular gitlink entry"
fi
git -C "$repo" reset --hard -q HEAD

ln -s ordinary.txt "$repo/tracked-link"
git -C "$repo" add -- tracked-link
if (assert_tracked_source_policy "$repo") 2>/dev/null; then
  die "tracked source policy accepted a symbolic link"
fi
git -C "$repo" reset --hard -q HEAD
rm -f -- "$repo/tracked-link"

printf 'fixture\n' > "$repo/forbidden.PfX"
git -C "$repo" add -- forbidden.PfX
if (assert_tracked_source_policy "$repo") 2>/dev/null; then
  die "tracked source policy accepted a forbidden certificate filename"
fi
git -C "$repo" reset --hard -q HEAD
rm -f -- "$repo/forbidden.PfX"

marker="$(printf '%s%s' '-----BE' 'GIN PRIVATE KEY-----')"
printf '%s\n' "$marker" > "$repo/forbidden-content.txt"
git -C "$repo" add -- forbidden-content.txt
if (assert_tracked_source_policy "$repo") 2>/dev/null; then
  die "tracked source policy accepted key content"
fi
git -C "$repo" reset --hard -q HEAD
rm -f -- "$repo/forbidden-content.txt"

local_secret_path="$(printf '%s/%s' 'Development' 'certificates')"
printf '%s\n' "$local_secret_path" > "$repo/forbidden-local-path.txt"
git -C "$repo" add -- forbidden-local-path.txt
if (assert_tracked_source_policy "$repo") 2>/dev/null; then
  die "tracked source policy accepted a forbidden local secret path"
fi

printf 'Tracked source policy fixtures passed.\n'
