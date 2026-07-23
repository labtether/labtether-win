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
UNSIGNED_ARCHIVE=""
OUTPUT_DIRECTORY=""
CONFIRM_SIGN=""

usage() {
  cat <<'USAGE'
Usage: scripts/release/sign-windows-release.sh \
  --tag TAG --agent-repo PATH --unsigned-archive PATH --output-directory PATH \
  --confirm-sign TAG

Signs an unsigned Windows payload on this Mac. The certificate path and
password are read silently from /dev/tty and are never accepted through
arguments or environment variables.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="${2:?--tag requires a value}"; shift 2 ;;
    --agent-repo) AGENT_REPO="${2:?--agent-repo requires a value}"; shift 2 ;;
    --unsigned-archive) UNSIGNED_ARCHIVE="${2:?--unsigned-archive requires a value}"; shift 2 ;;
    --output-directory) OUTPUT_DIRECTORY="${2:?--output-directory requires a value}"; shift 2 ;;
    --confirm-sign) CONFIRM_SIGN="${2:?--confirm-sign requires a value}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option" ;;
  esac
done

[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "release tag must use the exact vX.Y.Z format"
[[ -n "$AGENT_REPO" && -n "$UNSIGNED_ARCHIVE" && -n "$OUTPUT_DIRECTORY" ]] || die "all release arguments are required"
[[ "$CONFIRM_SIGN" == "$TAG" ]] || die "signing requires exact --confirm-sign TAG confirmation"
require_command git
require_command jq
require_command osslsigncode
require_command python3
require_command shasum
require_command zip

AGENT_REPO="$(cd "$AGENT_REPO" && pwd -P)"
UNSIGNED_ARCHIVE="$(canonical_existing_path "$UNSIGNED_ARCHIVE")"
[[ -f "$UNSIGNED_ARCHIVE" && ! -L "$UNSIGNED_ARCHIVE" ]] || die "unsigned release archive is unavailable"
[[ "$(basename "$UNSIGNED_ARCHIVE")" == "$UNSIGNED_ARCHIVE_NAME" ]] || die "unsigned release archive has an unexpected name"
assert_external_path "$UNSIGNED_ARCHIVE" "$REPO_ROOT" "$AGENT_REPO"

output_parent="$(cd "$(dirname "$OUTPUT_DIRECTORY")" && pwd -P)"
OUTPUT_DIRECTORY="$output_parent/$(basename "$OUTPUT_DIRECTORY")"
assert_external_path "$OUTPUT_DIRECTORY" "$REPO_ROOT" "$AGENT_REPO"
if [[ -e "$OUTPUT_DIRECTORY" ]]; then
  [[ -d "$OUTPUT_DIRECTORY" && ! -L "$OUTPUT_DIRECTORY" ]] || die "output path must be a directory"
  [[ -z "$(find "$OUTPUT_DIRECTORY" -mindepth 1 -maxdepth 1 -print -quit)" ]] || die "output directory must be empty"
  chmod 700 "$OUTPUT_DIRECTORY"
else
  mkdir -m 700 -- "$OUTPUT_DIRECTORY"
fi
assert_private_directory "$OUTPUT_DIRECTORY"

wrapper_commit="$(assert_clean_tagged_source "$REPO_ROOT" "$TAG")"
agent_commit="$(assert_clean_tagged_source "$AGENT_REPO" "$TAG")"
assert_tracked_source_policy "$REPO_ROOT"
assert_tracked_source_policy "$AGENT_REPO"

scratch_root="$(mktemp -d "${TMPDIR:-/tmp}/labtether-win-sign.XXXXXX")"
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

payload_root="$scratch_root/payload"
mkdir -m 700 -- "$payload_root"
extract_archive_safely "$UNSIGNED_ARCHIVE" "$payload_root"
validate_provenance "$payload_root" "$TAG" "$wrapper_commit" "$agent_commit"
validate_unsigned_payload_tree "$payload_root"
[[ ! -e "$payload_root/$SIGNED_PAYLOAD_MANIFEST_NAME" ]] || die "unsigned payload already contains signed metadata"

for relative_path in "$AUTHORED_PAYLOAD_1" "$AUTHORED_PAYLOAD_2" "$AUTHORED_PAYLOAD_3"; do
  payload_path="$payload_root/$relative_path"
  [[ -f "$payload_path" && ! -L "$payload_path" ]] || die "unsigned payload is missing a required authored file"
  expected_unsigned_hash="$(jq -r --arg path "$relative_path" '.authored_payloads[] | select(.path == $path) | .sha256' "$payload_root/release-provenance.json")"
  [[ "$expected_unsigned_hash" =~ ^[0-9a-f]{64}$ ]] || die "unsigned payload provenance is incomplete"
  [[ "$(sha256_file "$payload_path")" == "$expected_unsigned_hash" ]] || die "unsigned payload differs from its provenance"
  if osslsigncode verify -in "$payload_path" >/dev/null 2>&1; then
    die "unsigned preparation already contains an Authenticode signature"
  fi
done

IFS= read -r -s -p "Local code-signing certificate path: " certificate_path </dev/tty
printf '\n' >/dev/tty
[[ -f "$certificate_path" && ! -L "$certificate_path" ]] || die "local code-signing certificate is unavailable"
certificate_path="$(canonical_existing_path "$certificate_path")"
assert_external_path "$certificate_path" "$REPO_ROOT" "$AGENT_REPO"
IFS= read -r -s -p "Local code-signing certificate password: " certificate_password </dev/tty
printf '\n' >/dev/tty
[[ -n "$certificate_password" ]] || die "local code-signing certificate password is required"

for relative_path in "$AUTHORED_PAYLOAD_1" "$AUTHORED_PAYLOAD_2" "$AUTHORED_PAYLOAD_3"; do
  input_path="$payload_root/$relative_path"
  output_path="$input_path.signed"
  { exec 9<"$certificate_path"; } 2>/dev/null || die "unable to open the local code-signing certificate"
  if ! printf '%s\n' "$certificate_password" | osslsigncode sign \
      -pkcs12 /dev/fd/9 \
      -readpass - \
      -h sha256 \
      -ts "http://timestamp.digicert.com" \
      -n "LabTether Agent" \
      -in "$input_path" \
      -out "$output_path" >/dev/null 2>&1; then
    exec 9<&-
    die "Authenticode signing failed"
  fi
  exec 9<&-
  mv -- "$output_path" "$input_path"
  osslsigncode verify -in "$input_path" >/dev/null 2>&1 || die "Authenticode verification failed after signing"
done
unset certificate_password
unset certificate_path

p1_size="$(stat -f '%z' -- "$payload_root/$AUTHORED_PAYLOAD_1")"
p2_size="$(stat -f '%z' -- "$payload_root/$AUTHORED_PAYLOAD_2")"
p3_size="$(stat -f '%z' -- "$payload_root/$AUTHORED_PAYLOAD_3")"
p1_hash="$(sha256_file "$payload_root/$AUTHORED_PAYLOAD_1")"
p2_hash="$(sha256_file "$payload_root/$AUTHORED_PAYLOAD_2")"
p3_hash="$(sha256_file "$payload_root/$AUTHORED_PAYLOAD_3")"
jq -n \
  --arg p1 "$AUTHORED_PAYLOAD_1" --argjson s1 "$p1_size" --arg h1 "$p1_hash" \
  --arg p2 "$AUTHORED_PAYLOAD_2" --argjson s2 "$p2_size" --arg h2 "$p2_hash" \
  --arg p3 "$AUTHORED_PAYLOAD_3" --argjson s3 "$p3_size" --arg h3 "$p3_hash" \
  '{schema_version: 1, payloads: [
    {path: $p1, size: $s1, sha256: $h1},
    {path: $p2, size: $s2, sha256: $h2},
    {path: $p3, size: $s3, sha256: $h3}
  ]}' > "$payload_root/$SIGNED_PAYLOAD_MANIFEST_NAME"

temporary_archive="$scratch_root/$FINAL_ARCHIVE_NAME"
(
  cd -- "$payload_root"
  zip -q -X -r "$temporary_archive" ./*
)
[[ -z "$(find "$OUTPUT_DIRECTORY" -mindepth 1 -maxdepth 1 -print -quit)" ]] || die "output directory changed before final asset placement"
mv -n -- "$temporary_archive" "$OUTPUT_DIRECTORY/$FINAL_ARCHIVE_NAME"
[[ ! -e "$temporary_archive" && -f "$OUTPUT_DIRECTORY/$FINAL_ARCHIVE_NAME" ]] || die "refusing to overwrite an existing release archive"
final_hash="$(sha256_file "$OUTPUT_DIRECTORY/$FINAL_ARCHIVE_NAME")"
( set -o noclobber; printf '%s  %s\n' "$final_hash" "$FINAL_ARCHIVE_NAME" > "$OUTPUT_DIRECTORY/$FINAL_CHECKSUM_NAME" ) || die "refusing to overwrite an existing release checksum"
assert_final_asset_allowlist "$OUTPUT_DIRECTORY"

verify_root="$scratch_root/final-verification"
mkdir -m 700 -- "$verify_root"
verify_final_archive_on_mac "$OUTPUT_DIRECTORY" "$TAG" "$wrapper_commit" "$agent_commit" "$verify_root"

printf 'Mac-local Authenticode signing and re-extraction verification passed.\n'
printf 'archive_sha256=%s\n' "$final_hash"
printf 'Next: transfer only the two allowlisted assets to Windows and run verify-signed-windows-release.ps1.\n'
