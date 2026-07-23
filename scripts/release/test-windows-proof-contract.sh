#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/release/windows-release-common.sh
source "$SCRIPT_DIR/windows-release-common.sh"

require_command jq

fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/labtether-proof-contract.XXXXXX")"
cleanup() {
  rm -rf -- "$fixture_root"
}
trap cleanup EXIT

tag="v1.2.3"
wrapper_commit="1111111111111111111111111111111111111111"
agent_commit="2222222222222222222222222222222222222222"
archive_hash="3333333333333333333333333333333333333333333333333333333333333333"
proof="$fixture_root/windows-proof.json"
manifest="$fixture_root/signed-payloads.json"

jq -n \
  --arg tag "$tag" --arg wrapper "$wrapper_commit" --arg agent "$agent_commit" --arg archive "$archive_hash" \
  '{schema_version: 1, status: "signed-windows-verification-pass", tag: $tag,
    wrapper_commit: $wrapper, agent_commit: $agent, archive_sha256: $archive,
    verified_payload_count: 3, payloads: [
      {path: "LabTetherAgent.exe", size: 101, sha256: ("a" * 64)},
      {path: "LabTetherAgent.dll", size: 102, sha256: ("b" * 64)},
      {path: "Assets/labtether-agent.exe", size: 103, sha256: ("c" * 64)}
    ]}' > "$proof"
jq '{schema_version: 1, payloads: .payloads}' "$proof" > "$manifest"

validate_windows_verification_proof "$proof" "$tag" "$wrapper_commit" "$agent_commit" "$archive_hash"
compare_windows_proof_payloads "$proof" "$manifest"

jq '.payloads[0].sha256 = ("d" * 64)' "$manifest" > "$fixture_root/tampered-manifest.json"
if (compare_windows_proof_payloads "$proof" "$fixture_root/tampered-manifest.json") 2>/dev/null; then
  die "cross-stage fixture accepted a changed signed payload hash"
fi

jq 'del(.payloads)' "$proof" > "$fixture_root/missing-payload-proof.json"
if (validate_windows_verification_proof "$fixture_root/missing-payload-proof.json" "$tag" "$wrapper_commit" "$agent_commit" "$archive_hash") 2>/dev/null; then
  die "Windows proof fixture accepted missing payload records"
fi

checksum_hash="4444444444444444444444444444444444444444444444444444444444444444"
release_json="$(jq -n \
  --arg tag "$tag" --arg archive "$FINAL_ARCHIVE_NAME" --arg checksum "$FINAL_CHECKSUM_NAME" \
  --arg archive_digest "sha256:$archive_hash" --arg checksum_digest "sha256:$checksum_hash" \
  '{draft: true, tag_name: $tag, assets: [
    {name: $archive, state: "uploaded", size: 1000, digest: $archive_digest},
    {name: $checksum, state: "uploaded", size: 94, digest: $checksum_digest}
  ]}')"
validate_github_release_asset_readback "$release_json" "$tag" true "$archive_hash" "$checksum_hash" 1000 94
published_json="$(jq '.draft = false' <<<"$release_json")"
validate_github_release_asset_readback "$published_json" "$tag" false "$archive_hash" "$checksum_hash" 1000 94
null_digest_json="$(jq '.assets[0].digest = null' <<<"$release_json")"
if validate_github_release_asset_readback "$null_digest_json" "$tag" true "$archive_hash" "$checksum_hash" 1000 94; then
  die "GitHub readback fixture accepted a missing asset digest"
fi
wrong_state_json="$(jq '.assets[1].state = "new"' <<<"$release_json")"
if validate_github_release_asset_readback "$wrong_state_json" "$tag" true "$archive_hash" "$checksum_hash" 1000 94; then
  die "GitHub readback fixture accepted a non-uploaded asset"
fi

publisher="$SCRIPT_DIR/publish-windows-release.sh"
signer="$SCRIPT_DIR/sign-windows-release.sh"
grep -Fq 'signing requires exact --confirm-sign TAG confirmation' "$signer" || die "signer lacks its separate exact confirmation"
grep -Fq -- '--confirm-draft TAG | --confirm-publish TAG' "$publisher" || die "publisher lacks separate draft and publish confirmations"
grep -Fq 'draft creation and publication require separate invocations' "$publisher" || die "publisher does not reject combined confirmations"
if "$publisher" \
  --tag "$tag" \
  --agent-repo "$fixture_root/missing-agent" \
  --assets-directory "$fixture_root/missing-assets" \
  --windows-proof "$fixture_root/missing-proof" \
  --confirm-draft "$tag" \
  --confirm-publish "$tag" \
  >"$fixture_root/combined-confirmations.log" 2>&1; then
  die "publisher accepted draft and publish confirmations together"
fi
grep -Fq 'draft creation and publication require separate invocations' "$fixture_root/combined-confirmations.log" || die "publisher did not reject combined confirmations before release checks"
draft_create_line="$(grep -n 'gh release create ' "$publisher" | cut -d: -f1)"
draft_exit_line="$(grep -n '^  exit 0$' "$publisher" | cut -d: -f1)"
publish_line="$(grep -n 'gh release edit ' "$publisher" | cut -d: -f1)"
[[ -n "$draft_create_line" && -n "$draft_exit_line" && -n "$publish_line" ]] || die "publisher boundary statements are incomplete"
(( draft_create_line < draft_exit_line && draft_exit_line < publish_line )) || die "publisher lacks an exit boundary between draft creation and publication"
grep -Fq 'fresh draft inspection failed; release remains a draft' "$publisher" || die "publisher lacks fresh pre-publication draft inspection"

printf 'Windows proof cross-stage contract tests passed.\n'
