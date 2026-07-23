#!/usr/bin/env bash
set -Eeuo pipefail
set +x
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd -P)"
# shellcheck source=scripts/release/windows-release-common.sh
source "$SCRIPT_DIR/windows-release-common.sh"

TAG=""
AGENT_REPO=""
ASSETS_DIRECTORY=""
WINDOWS_PROOF=""
CONFIRM_PUBLISH=""

usage() {
  cat <<'USAGE'
Usage: scripts/release/publish-windows-release.sh \
  --tag TAG --agent-repo PATH --assets-directory PATH \
  --windows-proof PATH --confirm-publish TAG

Re-verifies clean tagged source, the exact two release assets, Mac-side
Authenticode validation, and the independent Windows proof before creating a
new GitHub release. It never overwrites an existing release.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="${2:?--tag requires a value}"; shift 2 ;;
    --agent-repo) AGENT_REPO="${2:?--agent-repo requires a value}"; shift 2 ;;
    --assets-directory) ASSETS_DIRECTORY="${2:?--assets-directory requires a value}"; shift 2 ;;
    --windows-proof) WINDOWS_PROOF="${2:?--windows-proof requires a value}"; shift 2 ;;
    --confirm-publish) CONFIRM_PUBLISH="${2:?--confirm-publish requires a value}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option" ;;
  esac
done

[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "release tag must use the exact vX.Y.Z format"
[[ -n "$AGENT_REPO" && -n "$ASSETS_DIRECTORY" && -n "$WINDOWS_PROOF" ]] || die "all release arguments are required"
[[ "$CONFIRM_PUBLISH" == "$TAG" ]] || die "publication requires exact --confirm-publish TAG confirmation"
require_command gh
require_command git
require_command jq
require_command osslsigncode
require_command python3
require_command shasum

AGENT_REPO="$(cd "$AGENT_REPO" && pwd -P)"
ASSETS_DIRECTORY="$(cd "$ASSETS_DIRECTORY" && pwd -P)"
WINDOWS_PROOF="$(canonical_existing_path "$WINDOWS_PROOF")"
[[ -f "$WINDOWS_PROOF" && ! -L "$WINDOWS_PROOF" ]] || die "Windows verification proof is unavailable"
assert_external_path "$ASSETS_DIRECTORY" "$REPO_ROOT" "$AGENT_REPO"
assert_external_path "$WINDOWS_PROOF" "$REPO_ROOT" "$AGENT_REPO"

wrapper_commit="$(assert_clean_tagged_source "$REPO_ROOT" "$TAG")"
agent_commit="$(assert_clean_tagged_source "$AGENT_REPO" "$TAG")"
assert_tracked_source_policy "$REPO_ROOT"
assert_tracked_source_policy "$AGENT_REPO"
assert_final_asset_allowlist "$ASSETS_DIRECTORY"
validate_checksum_asset "$ASSETS_DIRECTORY"
archive_hash="$(sha256_file "$ASSETS_DIRECTORY/$FINAL_ARCHIVE_NAME")"

validate_windows_verification_proof "$WINDOWS_PROOF" "$TAG" "$wrapper_commit" "$agent_commit" "$archive_hash"

scratch_root="$(mktemp -d "${TMPDIR:-/tmp}/labtether-win-publish.XXXXXX")"
scratch_root="$(cd "$scratch_root" && pwd -P)"
assert_external_path "$scratch_root" "$REPO_ROOT" "$AGENT_REPO"
assert_private_directory "$scratch_root"
cleanup() {
  if [[ -n "${scratch_root:-}" && -d "$scratch_root" ]]; then
    assert_external_path "$scratch_root" "$REPO_ROOT" "$AGENT_REPO"
    rm -rf -- "$scratch_root"
  fi
}
trap cleanup EXIT
verify_final_archive_on_mac "$ASSETS_DIRECTORY" "$TAG" "$wrapper_commit" "$agent_commit" "$scratch_root"
compare_windows_proof_payloads "$WINDOWS_PROOF" "$scratch_root/extracted/$SIGNED_PAYLOAD_MANIFEST_NAME"

remote_tag_commit() {
  local repository="$1"
  local tag="$2"
  local refs
  local commit
  refs="$(git -C "$repository" ls-remote --tags origin "refs/tags/$tag" "refs/tags/$tag^{}")"
  commit="$(printf '%s\n' "$refs" | awk '$2 ~ /\^\{\}$/ { print $1 }' | tail -n 1)"
  if [[ -z "$commit" ]]; then
    commit="$(printf '%s\n' "$refs" | awk -v ref="refs/tags/$tag" '$2 == ref { print $1 }' | tail -n 1)"
  fi
  [[ -n "$commit" ]] || die "release tag is unavailable on the source remote"
  printf '%s\n' "$commit"
}

remote_wrapper_commit="$(remote_tag_commit "$REPO_ROOT" "$TAG")"
remote_agent_commit="$(remote_tag_commit "$AGENT_REPO" "$TAG")"
[[ "$remote_wrapper_commit" == "$wrapper_commit" ]] || die "remote wrapper tag differs from verified local source"
[[ "$remote_agent_commit" == "$agent_commit" ]] || die "remote agent tag differs from verified local source"

if gh api "repos/labtether/labtether-win/releases/tags/$TAG" >/dev/null 2>&1; then
  die "GitHub release already exists; refusing to overwrite it"
fi

gh release create "$TAG" \
  "$ASSETS_DIRECTORY/$FINAL_ARCHIVE_NAME" \
  "$ASSETS_DIRECTORY/$FINAL_CHECKSUM_NAME" \
  --repo labtether/labtether-win \
  --verify-tag \
  --draft \
  --title "LabTether Windows Agent ${TAG#v}" \
  --generate-notes

archive_size="$(stat -f '%z' -- "$ASSETS_DIRECTORY/$FINAL_ARCHIVE_NAME")"
checksum_size="$(stat -f '%z' -- "$ASSETS_DIRECTORY/$FINAL_CHECKSUM_NAME")"
checksum_asset_hash="$(sha256_file "$ASSETS_DIRECTORY/$FINAL_CHECKSUM_NAME")"
draft_json="$(gh api "repos/labtether/labtether-win/releases/tags/$TAG")"
if ! validate_github_release_asset_readback "$draft_json" "$TAG" true "$archive_hash" "$checksum_asset_hash" "$archive_size" "$checksum_size"; then
  die "draft release inspection failed; release remains a draft"
fi

gh release edit "$TAG" --repo labtether/labtether-win --draft=false >/dev/null
published_json="$(gh api "repos/labtether/labtether-win/releases/tags/$TAG")"
validate_github_release_asset_readback "$published_json" "$TAG" false "$archive_hash" "$checksum_asset_hash" "$archive_size" "$checksum_size" || die "published release readback failed"

printf 'Published exactly two verified Windows release assets for %s after draft inspection.\n' "$TAG"
