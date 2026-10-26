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
SIGNED_ASSETS=""
UNSIGNED_MSI=""
OUTPUT_DIRECTORY=""
CONFIRM_SIGN=""

usage() {
  cat <<'USAGE'
Usage: scripts/release/sign-windows-release.sh \
  --tag TAG --agent-repo PATH --output-directory PATH --confirm-sign TAG \
  (--unsigned-archive PATH | --signed-assets PATH --unsigned-msi PATH)

Signs a Windows release on this Mac in two separate invocations:
  --unsigned-archive  signs the three authored PE files and emits the
                      release ZIP and its checksum.
  --signed-assets with --unsigned-msi
                      signs the MSI that Windows verification built from
                      that signed ZIP and emits all four release assets.
The certificate path and password are read silently from /dev/tty and are
never accepted through arguments or environment variables.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="${2:?--tag requires a value}"; shift 2 ;;
    --agent-repo) AGENT_REPO="${2:?--agent-repo requires a value}"; shift 2 ;;
    --unsigned-archive) UNSIGNED_ARCHIVE="${2:?--unsigned-archive requires a value}"; shift 2 ;;
    --signed-assets) SIGNED_ASSETS="${2:?--signed-assets requires a value}"; shift 2 ;;
    --unsigned-msi) UNSIGNED_MSI="${2:?--unsigned-msi requires a value}"; shift 2 ;;
    --output-directory) OUTPUT_DIRECTORY="${2:?--output-directory requires a value}"; shift 2 ;;
    --confirm-sign) CONFIRM_SIGN="${2:?--confirm-sign requires a value}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option" ;;
  esac
done

[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "release tag must use the exact vX.Y.Z format"
[[ -n "$AGENT_REPO" && -n "$OUTPUT_DIRECTORY" ]] || die "all release arguments are required"
if [[ -n "$UNSIGNED_ARCHIVE" ]]; then
  [[ -z "$SIGNED_ASSETS" && -z "$UNSIGNED_MSI" ]] || die "sign the archive and the MSI in separate invocations"
  SIGN_STAGE="archive"
else
  [[ -n "$SIGNED_ASSETS" && -n "$UNSIGNED_MSI" ]] || die "all release arguments are required"
  SIGN_STAGE="msi"
fi
[[ "$CONFIRM_SIGN" == "$TAG" ]] || die "signing requires exact --confirm-sign TAG confirmation"
require_command git
require_command jq
require_command osslsigncode
require_command python3
require_command shasum
require_command zip

AGENT_REPO="$(cd "$AGENT_REPO" && pwd -P)"
if [[ "$SIGN_STAGE" == "archive" ]]; then
  UNSIGNED_ARCHIVE="$(canonical_existing_path "$UNSIGNED_ARCHIVE")"
  [[ -f "$UNSIGNED_ARCHIVE" && ! -L "$UNSIGNED_ARCHIVE" ]] || die "unsigned release archive is unavailable"
  [[ "$(basename "$UNSIGNED_ARCHIVE")" == "$UNSIGNED_ARCHIVE_NAME" ]] || die "unsigned release archive has an unexpected name"
  assert_external_path "$UNSIGNED_ARCHIVE" "$REPO_ROOT" "$AGENT_REPO"
else
  SIGNED_ASSETS="$(cd "$SIGNED_ASSETS" && pwd -P)"
  UNSIGNED_MSI="$(canonical_existing_path "$UNSIGNED_MSI")"
  [[ -f "$UNSIGNED_MSI" && ! -L "$UNSIGNED_MSI" ]] || die "unsigned MSI is unavailable"
  [[ "$(basename "$UNSIGNED_MSI")" == "$UNSIGNED_MSI_NAME" ]] || die "unsigned MSI has an unexpected name"
  assert_external_path "$SIGNED_ASSETS" "$REPO_ROOT" "$AGENT_REPO"
  assert_external_path "$UNSIGNED_MSI" "$REPO_ROOT" "$AGENT_REPO"
fi

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
  unset certificate_password
  if [[ -n "${scratch_root:-}" && -d "$scratch_root" ]]; then
    assert_external_path "$scratch_root" "$REPO_ROOT" "$AGENT_REPO"
    rm -rf -- "$scratch_root"
  fi
}
trap cleanup EXIT

read_signing_identity() {
  IFS= read -r -s -p "Local code-signing certificate path: " certificate_path </dev/tty
  printf '\n' >/dev/tty
  [[ -f "$certificate_path" && ! -L "$certificate_path" ]] || die "local code-signing certificate is unavailable"
  certificate_path="$(canonical_existing_path "$certificate_path")"
  assert_external_path "$certificate_path" "$REPO_ROOT" "$AGENT_REPO"
  IFS= read -r -s -p "Local code-signing certificate password: " certificate_password </dev/tty
  printf '\n' >/dev/tty
  [[ -n "$certificate_password" ]] || die "local code-signing certificate password is required"
}

forget_signing_identity() {
  unset certificate_password
  unset certificate_path
}

# Writes a signed copy of a PE or MSI file and checks the signature.
authenticode_sign() {
  local input_path="$1"
  local output_path="$2"
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
  osslsigncode verify -in "$output_path" >/dev/null 2>&1 || die "Authenticode verification failed after signing"
}

sign_archive_stage() {
  local payload_root="$scratch_root/payload"
  local relative_path payload_path expected_unsigned_hash
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

  read_signing_identity
  for relative_path in "$AUTHORED_PAYLOAD_1" "$AUTHORED_PAYLOAD_2" "$AUTHORED_PAYLOAD_3"; do
    authenticode_sign "$payload_root/$relative_path" "$payload_root/$relative_path.signed"
    mv -- "$payload_root/$relative_path.signed" "$payload_root/$relative_path"
  done
  forget_signing_identity

  local p1_size p2_size p3_size p1_hash p2_hash p3_hash
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

  local temporary_archive="$scratch_root/$FINAL_ARCHIVE_NAME"
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

  local verify_root="$scratch_root/final-verification"
  mkdir -m 700 -- "$verify_root"
  verify_final_archive_on_mac "$OUTPUT_DIRECTORY" "$TAG" "$wrapper_commit" "$agent_commit" "$verify_root"

  printf 'Mac-local Authenticode signing and re-extraction verification passed.\n'
  printf 'archive_sha256=%s\n' "$final_hash"
  printf 'Next: on Windows, run verify-signed-windows-release.ps1 on these two assets with -UnsignedMsiOutputPath, then sign that MSI here with --signed-assets and --unsigned-msi.\n'
}

sign_msi_stage() {
  local archive_check="$scratch_root/signed-archive-check"
  local staging="$scratch_root/assets"
  local payload_signer name
  mkdir -m 700 -- "$archive_check" "$staging"
  # The MSI must be signed by the same identity as the verified ZIP payload.
  verify_final_archive_on_mac "$SIGNED_ASSETS" "$TAG" "$wrapper_commit" "$agent_commit" "$archive_check"
  payload_signer="$VERIFIED_SIGNER_ID"
  if osslsigncode verify -in "$UNSIGNED_MSI" >/dev/null 2>&1; then
    die "unsigned MSI already contains an Authenticode signature"
  fi
  cp -- "$SIGNED_ASSETS/$FINAL_ARCHIVE_NAME" "$SIGNED_ASSETS/$FINAL_CHECKSUM_NAME" "$staging/"

  read_signing_identity
  authenticode_sign "$UNSIGNED_MSI" "$staging/$FINAL_MSI_NAME"
  forget_signing_identity
  verify_signed_msi_with_osslsigncode "$staging/$FINAL_MSI_NAME" "$payload_signer"
  msi_hash="$(sha256_file "$staging/$FINAL_MSI_NAME")"
  printf '%s  %s\n' "$msi_hash" "$FINAL_MSI_NAME" > "$staging/$FINAL_MSI_CHECKSUM_NAME"
  validate_checksum_asset "$staging"
  validate_checksum_asset "$staging" "$FINAL_MSI_NAME"

  [[ -z "$(find "$OUTPUT_DIRECTORY" -mindepth 1 -maxdepth 1 -print -quit)" ]] || die "output directory changed before final asset placement"
  for name in "$FINAL_ARCHIVE_NAME" "$FINAL_CHECKSUM_NAME" "$FINAL_MSI_NAME" "$FINAL_MSI_CHECKSUM_NAME"; do
    mv -n -- "$staging/$name" "$OUTPUT_DIRECTORY/$name"
    [[ ! -e "$staging/$name" && -f "$OUTPUT_DIRECTORY/$name" ]] || die "refusing to overwrite an existing release asset"
  done
  assert_final_asset_allowlist "$OUTPUT_DIRECTORY" with-msi

  local verify_root="$scratch_root/final-verification"
  mkdir -m 700 -- "$verify_root"
  verify_final_archive_on_mac "$OUTPUT_DIRECTORY" "$TAG" "$wrapper_commit" "$agent_commit" "$verify_root" with-msi

  printf 'Mac-local MSI signing and four-asset verification passed.\n'
  printf 'msi_sha256=%s\n' "$msi_hash"
  printf 'Next: transfer only the four allowlisted assets to Windows and run verify-signed-windows-release.ps1.\n'
}

if [[ "$SIGN_STAGE" == "archive" ]]; then
  sign_archive_stage
else
  sign_msi_stage
fi
