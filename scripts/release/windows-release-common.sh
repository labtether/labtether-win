#!/usr/bin/env bash

set -Eeuo pipefail

WINDOWS_RELEASE_COMMON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
readonly WINDOWS_RELEASE_COMMON_DIR
readonly FINAL_ARCHIVE_NAME="labtether-agent-win-x64.zip"
readonly FINAL_CHECKSUM_NAME="labtether-agent-win-x64.zip.sha256"
# Used by the signing entry point after this file is sourced.
# shellcheck disable=SC2034
readonly UNSIGNED_ARCHIVE_NAME="labtether-agent-win-x64-unsigned.zip"
readonly SIGNED_PAYLOAD_MANIFEST_NAME="signed-payloads.json"
readonly AUTHORED_PAYLOAD_1="LabTetherAgent.exe"
readonly AUTHORED_PAYLOAD_2="LabTetherAgent.dll"
readonly AUTHORED_PAYLOAD_3="Assets/labtether-agent.exe"

die() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "missing required command: $1"
}

canonical_existing_path() {
  local input="$1"
  local directory
  local leaf
  directory="$(dirname -- "$input")"
  leaf="$(basename -- "$input")"
  (cd -- "$directory" && printf '%s/%s\n' "$(pwd -P)" "$leaf")
}

path_is_within() {
  local candidate="$1"
  local parent="$2"
  case "${candidate}/" in
    "${parent}/"*) return 0 ;;
    *) return 1 ;;
  esac
}

assert_external_path() {
  local candidate="$1"
  shift
  local protected_root
  for protected_root in "$@"; do
    if path_is_within "$candidate" "$protected_root"; then
      die "release staging must remain outside source repositories"
    fi
  done
}

assert_clean_tagged_source() {
  local repository="$1"
  local tag="$2"
  local status
  local head_sha
  local tag_sha
  status="$(git -C "$repository" status --porcelain=v1 --untracked-files=all)"
  [[ -z "$status" ]] || die "release source checkout is not clean"
  head_sha="$(git -C "$repository" rev-parse HEAD)"
  tag_sha="$(git -C "$repository" rev-list -n 1 "$tag")"
  [[ "$head_sha" == "$tag_sha" ]] || die "release source checkout is not at the requested tag"
  printf '%s\n' "$head_sha"
}

assert_tracked_source_policy() {
  local repository="$1"
  local index_entry
  local mode
  local tracked_path
  local grep_status
  local pem_pattern
  local local_secret_path
  local content_pattern
  git -C "$repository" rev-parse --is-inside-work-tree >/dev/null 2>&1 || die "unable to inspect tracked release source"
  while IFS= read -r -d '' index_entry; do
    [[ "$index_entry" != "LABTETHER_GIT_LS_FILES_FAILED" ]] || die "unable to inspect tracked release source"
    mode="${index_entry%% *}"
    tracked_path="${index_entry#*$'\t'}"
    case "$mode" in
      100644|100755) ;;
      *) die "release source contains a non-regular tracked entry" ;;
    esac
    if [[ "$tracked_path" =~ \.([Pp][Ff][Xx]|[Pp]12|[Pp][Kk][Cc][Ss]12|[Cc][Ee][Rr]|[Cc][Rr][Tt]|[Dd][Ee][Rr]|[Pp][Ee][Mm]|[Kk][Ee][Yy]|[Jj][Kk][Ss]|[Kk][Ee][Yy][Ss][Tt][Oo][Rr][Ee]|[Kk][Dd][Bb]|[Pp][Pp][Kk])$ ]]; then
      die "release source contains a forbidden certificate or key filename"
    fi
  done < <(git -C "$repository" ls-files -s -z || printf 'LABTETHER_GIT_LS_FILES_FAILED\0')
  pem_pattern="$(printf '%s%s' '-----BE' 'GIN ([A-Z0-9 ]*PRIVATE KEY|CERTIFICATE)-----')"
  local_secret_path="$(printf '%s/%s' 'Development' 'certificates')"
  content_pattern="(${pem_pattern}|(^|[/~])${local_secret_path}([/]|$))"
  if git -C "$repository" grep -I -q -E -- "$content_pattern" -- .; then
    die "release source contains forbidden certificate, key, or local secret-path content"
  else
    grep_status=$?
    [[ "$grep_status" == "1" ]] || die "unable to scan tracked release source"
  fi
}

assert_safe_manifest_path() {
  local path="$1"
  [[ -n "$path" && "$path" != /* && "$path" != *\\* && "$path" != *//* && ! "$path" =~ ^[A-Za-z]: ]] || die "release manifest contains an unsafe path"
  case "$path" in
    .|..|./*|../*|*/./*|*/.|*/../*|*/..|*/) die "release manifest contains an unsafe path" ;;
  esac
}

sha256_file() {
  shasum -a 256 -- "$1" | awk '{print tolower($1)}'
}

assert_private_directory() {
  local directory="$1"
  local mode
  [[ -d "$directory" && ! -L "$directory" ]] || die "private release directory is unavailable"
  mode="$(stat -f '%Lp' -- "$directory")"
  [[ "$mode" == "700" ]] || die "private release directory must have mode 0700"
}

assert_final_asset_allowlist() {
  local directory="$1"
  local count
  [[ -f "$directory/$FINAL_ARCHIVE_NAME" && ! -L "$directory/$FINAL_ARCHIVE_NAME" ]] || die "final release archive is missing"
  [[ -f "$directory/$FINAL_CHECKSUM_NAME" && ! -L "$directory/$FINAL_CHECKSUM_NAME" ]] || die "final release checksum is missing"
  count="$(find "$directory" -mindepth 1 -maxdepth 1 -print | wc -l | tr -d ' ')"
  [[ "$count" == "2" ]] || die "final release directory must contain exactly the two allowed assets"
}

validate_checksum_asset() {
  local directory="$1"
  local checksum_path="$directory/$FINAL_CHECKSUM_NAME"
  local checksum_line
  local expected
  local actual
  [[ "$(wc -l < "$checksum_path" | tr -d ' ')" == "1" ]] || die "checksum asset must contain exactly one line"
  checksum_line="$(tr -d '\n' < "$checksum_path")"
  printf '%s\n' "$checksum_line" | grep -Eq '^[0-9A-Fa-f]{64}  labtether-agent-win-x64\.zip$' || die "checksum asset has an invalid format"
  expected="$(printf '%s\n' "$checksum_line" | awk '{print tolower($1)}')"
  actual="$(sha256_file "$directory/$FINAL_ARCHIVE_NAME")"
  [[ "$actual" == "$expected" ]] || die "release archive checksum mismatch"
}

extract_archive_safely() {
  local archive="$1"
  local destination="$2"
  local extractor="$WINDOWS_RELEASE_COMMON_DIR/safe_extract_windows_release.py"
  [[ -f "$extractor" && ! -L "$extractor" ]] || die "safe release archive extractor is unavailable"
  [[ -d "$destination" && ! -L "$destination" ]] || die "release extraction directory is unavailable"
  [[ -z "$(find "$destination" -mindepth 1 -maxdepth 1 -print -quit)" ]] || die "release extraction directory must be empty"
  python3 "$extractor" "$archive" "$destination" || die "release archive failed safe extraction policy"
}

validate_provenance() {
  local payload_root="$1"
  local tag="$2"
  local wrapper_commit="$3"
  local agent_commit="$4"
  local provenance="$payload_root/release-provenance.json"
  [[ -f "$provenance" && ! -L "$provenance" ]] || die "release provenance is missing"
  jq -e \
    --arg tag "$tag" \
    --arg wrapper "$wrapper_commit" \
    --arg agent "$agent_commit" \
    '.schema_version == 1 and
     .payload == "labtether-agent-win-x64-unsigned" and
     .tag == $tag and
     .wrapper_commit == $wrapper and
     .agent_commit == $agent and
     (.authored_payloads | length) == 3 and
     ([.authored_payloads[].path] | sort) == (["Assets/labtether-agent.exe", "LabTetherAgent.dll", "LabTetherAgent.exe"] | sort) and
     all(.authored_payloads[]; (.size | type) == "number" and .size >= 0 and (.sha256 | test("^[0-9a-f]{64}$"))) and
     (.published_files | type) == "array" and
     .published_file_count >= 1 and .published_file_count <= 2048 and
     (.published_files | length) == .published_file_count and
     ([.published_files[].path | ascii_downcase] | unique | length) == .published_file_count and
     ([.published_files[].size] | add) == .published_bytes and
     .published_bytes >= 1 and .published_bytes <= 536870912 and
     all(.published_files[]; (.path | type) == "string" and length > 0 and .path != "release-provenance.json" and .path != "signed-payloads.json" and (.size | type) == "number" and .size >= 0 and .size <= 268435456 and (.sha256 | test("^[0-9a-f]{64}$"))) and
     all(.authored_payloads[] as $authored; any(.published_files[]; .path == $authored.path and .size == $authored.size and .sha256 == $authored.sha256))' \
    "$provenance" >/dev/null || die "release provenance does not match clean tagged sources"
  [[ "$(tr -d '\r\n' < "$payload_root/AGENT_VERSION")" == "$tag" ]] || die "bundled child version marker does not match the release tag"
}

validate_unsigned_payload_tree() {
  local payload_root="$1"
  local provenance="$payload_root/release-provenance.json"
  local expected_count
  local actual_count
  local record
  local path
  local expected_size
  local expected_hash
  expected_count="$(jq -r '.published_file_count' "$provenance")"
  actual_count="$(find "$payload_root" -type f | wc -l | tr -d ' ')"
  [[ "$actual_count" == "$((expected_count + 1))" ]] || die "unsigned payload contains an unexpected file set"
  while IFS= read -r record; do
    path="$(printf '%s' "$record" | jq -r '.path')"
    expected_size="$(printf '%s' "$record" | jq -r '.size')"
    expected_hash="$(printf '%s' "$record" | jq -r '.sha256')"
    assert_safe_manifest_path "$path"
    [[ -f "$payload_root/$path" && ! -L "$payload_root/$path" ]] || die "unsigned payload is missing a provenance file"
    [[ "$(stat -f '%z' -- "$payload_root/$path")" == "$expected_size" ]] || die "unsigned payload size differs from provenance"
    [[ "$(sha256_file "$payload_root/$path")" == "$expected_hash" ]] || die "unsigned payload hash differs from provenance"
  done < <(jq -c '.published_files[]' "$provenance")
  if find "$payload_root" -mindepth 1 -type d -empty -print -quit | grep -q .; then
    die "unsigned payload contains an unexpected empty directory"
  fi
}

validate_signed_payload_manifest() {
  local payload_root="$1"
  local manifest="$payload_root/$SIGNED_PAYLOAD_MANIFEST_NAME"
  [[ -f "$manifest" && ! -L "$manifest" ]] || die "signed payload manifest is missing"
  jq -e '
    .schema_version == 1 and
    (.payloads | length) == 3 and
    ([.payloads[].path] | sort) == (["Assets/labtether-agent.exe", "LabTetherAgent.dll", "LabTetherAgent.exe"] | sort) and
    ([.payloads[].path | ascii_downcase] | unique | length) == 3 and
    all(.payloads[]; (.size | type) == "number" and .size > 0 and (.sha256 | test("^[0-9a-f]{64}$")))' \
    "$manifest" >/dev/null || die "signed payload manifest is invalid"
}

validate_windows_verification_proof() {
  local proof="$1"
  local tag="$2"
  local wrapper_commit="$3"
  local agent_commit="$4"
  local archive_hash="$5"
  [[ -f "$proof" && ! -L "$proof" ]] || die "Windows verification proof is unavailable"
  jq -e \
    --arg tag "$tag" \
    --arg wrapper "$wrapper_commit" \
    --arg agent "$agent_commit" \
    --arg archive "$archive_hash" \
    '.schema_version == 1 and
     .status == "signed-windows-verification-pass" and
     .tag == $tag and
     .wrapper_commit == $wrapper and
     .agent_commit == $agent and
     .archive_sha256 == $archive and
     .verified_payload_count == 3 and
     (.payloads | type) == "array" and
     (.payloads | length) == 3 and
     ([.payloads[].path] | sort) == (["Assets/labtether-agent.exe", "LabTetherAgent.dll", "LabTetherAgent.exe"] | sort) and
     ([.payloads[].path | ascii_downcase] | unique | length) == 3 and
     all(.payloads[]; (.size | type) == "number" and .size > 0 and (.sha256 | test("^[0-9a-f]{64}$")))' \
    "$proof" >/dev/null || die "Windows verification proof does not match the release"
}

compare_windows_proof_payloads() {
  local proof="$1"
  local signed_manifest="$2"
  jq -e \
    --slurpfile signed "$signed_manifest" \
    '([.payloads[]] | sort_by(.path)) == ([$signed[0].payloads[]] | sort_by(.path))' \
    "$proof" >/dev/null || die "Windows proof payload hashes differ from the signed archive manifest"
}

validate_github_release_asset_readback() {
  local release_json="$1"
  local tag="$2"
  local expected_draft="$3"
  local archive_hash="$4"
  local checksum_hash="$5"
  local archive_size="$6"
  local checksum_size="$7"
  jq -e \
    --arg tag "$tag" \
    --arg archive "$FINAL_ARCHIVE_NAME" \
    --arg checksum "$FINAL_CHECKSUM_NAME" \
    --arg archive_digest "sha256:$archive_hash" \
    --arg checksum_digest "sha256:$checksum_hash" \
    --argjson expected_draft "$expected_draft" \
    --argjson archive_size "$archive_size" \
    --argjson checksum_size "$checksum_size" \
    '.draft == $expected_draft and
     .tag_name == $tag and
     (.assets | length) == 2 and
     ([.assets[].name] | sort) == ([$archive, $checksum] | sort) and
     any(.assets[]; .name == $archive and .state == "uploaded" and .size == $archive_size and .digest == $archive_digest) and
     any(.assets[]; .name == $checksum and .state == "uploaded" and .size == $checksum_size and .digest == $checksum_digest)' \
    <<<"$release_json" >/dev/null
}

validate_final_payload_tree() {
  local payload_root="$1"
  local provenance="$payload_root/release-provenance.json"
  local signed_manifest="$payload_root/$SIGNED_PAYLOAD_MANIFEST_NAME"
  local published_count
  local actual_count
  local record
  local path
  local expected_size
  local expected_hash
  published_count="$(jq -r '.published_file_count' "$provenance")"
  actual_count="$(find "$payload_root" -type f | wc -l | tr -d ' ')"
  [[ "$actual_count" == "$((published_count + 2))" ]] || die "signed payload contains an unexpected file set"
  while IFS= read -r record; do
    path="$(printf '%s' "$record" | jq -r '.path')"
    assert_safe_manifest_path "$path"
    case "$path" in
      "$AUTHORED_PAYLOAD_1"|"$AUTHORED_PAYLOAD_2"|"$AUTHORED_PAYLOAD_3") continue ;;
    esac
    expected_size="$(printf '%s' "$record" | jq -r '.size')"
    expected_hash="$(printf '%s' "$record" | jq -r '.sha256')"
    [[ -f "$payload_root/$path" && ! -L "$payload_root/$path" ]] || die "signed payload is missing a provenance file"
    [[ "$(stat -f '%z' -- "$payload_root/$path")" == "$expected_size" ]] || die "signed payload changed a non-authored file size"
    [[ "$(sha256_file "$payload_root/$path")" == "$expected_hash" ]] || die "signed payload changed a non-authored file hash"
  done < <(jq -c '.published_files[]' "$provenance")
  while IFS= read -r record; do
    path="$(printf '%s' "$record" | jq -r '.path')"
    assert_safe_manifest_path "$path"
    expected_size="$(printf '%s' "$record" | jq -r '.size')"
    expected_hash="$(printf '%s' "$record" | jq -r '.sha256')"
    [[ -f "$payload_root/$path" && ! -L "$payload_root/$path" ]] || die "signed payload is missing an authored file"
    [[ "$(stat -f '%z' -- "$payload_root/$path")" == "$expected_size" ]] || die "signed authored payload size differs from its manifest"
    [[ "$(sha256_file "$payload_root/$path")" == "$expected_hash" ]] || die "signed authored payload hash differs from its manifest"
  done < <(jq -c '.payloads[]' "$signed_manifest")
  if find "$payload_root" -mindepth 1 -type d -empty -print -quit | grep -q .; then
    die "signed payload contains an unexpected empty directory"
  fi
}

verify_signed_payloads_with_osslsigncode() {
  local payload_root="$1"
  local relative_path
  local verify_output
  local signer_id
  local expected_signer_id=""
  local signature_count
  for relative_path in "$AUTHORED_PAYLOAD_1" "$AUTHORED_PAYLOAD_2" "$AUTHORED_PAYLOAD_3"; do
    [[ -f "$payload_root/$relative_path" && ! -L "$payload_root/$relative_path" ]] || die "required signed payload is missing"
    verify_output="$(osslsigncode verify -verbose -in "$payload_root/$relative_path" 2>&1)" || die "Authenticode verification failed"
    signature_count="$(printf '%s\n' "$verify_output" | grep -c '^Signature Index:')"
    [[ "$signature_count" == "1" ]] || die "signed payload must contain exactly one Authenticode signature"
    printf '%s\n' "$verify_output" | grep -q '^Timestamp Server Signature verification: ok$' || die "Authenticode timestamp verification failed"
    printf '%s\n' "$verify_output" | grep -q '^Signature verification: ok$' || die "Authenticode signature verification failed"
    printf '%s\n' "$verify_output" | grep -q '^Number of verified signatures: 1$' || die "Authenticode signature count verification failed"
    signer_id="$(printf '%s\n' "$verify_output" | awk '
      /^Signer.s certificate:/ { in_signer = 1; next }
      in_signer && /Serial :/ { sub(/^.*Serial :[[:space:]]*/, ""); print; exit }
    ')"
    [[ -n "$signer_id" ]] || die "unable to identify the Authenticode signer"
    if [[ -z "$expected_signer_id" ]]; then
      expected_signer_id="$signer_id"
    else
      [[ "$signer_id" == "$expected_signer_id" ]] || die "authored payloads do not share one Authenticode signer"
    fi
  done
}

verify_final_archive_on_mac() {
  local assets_directory="$1"
  local tag="$2"
  local wrapper_commit="$3"
  local agent_commit="$4"
  local scratch_root="$5"
  local extract_root="$scratch_root/extracted"
  mkdir -m 700 -- "$extract_root"
  assert_final_asset_allowlist "$assets_directory"
  validate_checksum_asset "$assets_directory"
  extract_archive_safely "$assets_directory/$FINAL_ARCHIVE_NAME" "$extract_root"
  validate_provenance "$extract_root" "$tag" "$wrapper_commit" "$agent_commit"
  validate_signed_payload_manifest "$extract_root"
  validate_final_payload_tree "$extract_root"
  verify_signed_payloads_with_osslsigncode "$extract_root"
}
