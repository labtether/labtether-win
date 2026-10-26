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
msi_hash="5555555555555555555555555555555555555555555555555555555555555555"
proof="$fixture_root/windows-proof.json"
manifest="$fixture_root/signed-payloads.json"

jq -n \
  --arg tag "$tag" --arg wrapper "$wrapper_commit" --arg agent "$agent_commit" --arg archive "$archive_hash" \
  --arg msi "$msi_hash" \
  '{schema_version: 1, status: "signed-windows-verification-pass", tag: $tag,
    wrapper_commit: $wrapper, agent_commit: $agent, archive_sha256: $archive, msi_sha256: $msi,
    verified_payload_count: 3, payloads: [
      {path: "LabTetherAgent.exe", size: 101, sha256: ("a" * 64)},
      {path: "LabTetherAgent.dll", size: 102, sha256: ("b" * 64)},
      {path: "Assets/labtether-agent.exe", size: 103, sha256: ("c" * 64)}
    ]}' > "$proof"
jq '{schema_version: 1, payloads: .payloads}' "$proof" > "$manifest"

validate_windows_verification_proof "$proof" "$tag" "$wrapper_commit" "$agent_commit" "$archive_hash"
validate_windows_verification_proof "$proof" "$tag" "$wrapper_commit" "$agent_commit" "$archive_hash" "$msi_hash"
compare_windows_proof_payloads "$proof" "$manifest"
if (validate_windows_verification_proof "$proof" "$tag" "$wrapper_commit" "$agent_commit" "$archive_hash" "${msi_hash//5/6}") 2>/dev/null; then
  die "Windows proof fixture accepted a different signed MSI"
fi
jq 'del(.msi_sha256)' "$proof" > "$fixture_root/zip-only-proof.json"
if (validate_windows_verification_proof "$fixture_root/zip-only-proof.json" "$tag" "$wrapper_commit" "$agent_commit" "$archive_hash" "$msi_hash") 2>/dev/null; then
  die "Windows proof fixture accepted a proof that does not cover the MSI"
fi

jq '.payloads[0].sha256 = ("d" * 64)' "$manifest" > "$fixture_root/tampered-manifest.json"
if (compare_windows_proof_payloads "$proof" "$fixture_root/tampered-manifest.json") 2>/dev/null; then
  die "cross-stage fixture accepted a changed signed payload hash"
fi

jq 'del(.payloads)' "$proof" > "$fixture_root/missing-payload-proof.json"
if (validate_windows_verification_proof "$fixture_root/missing-payload-proof.json" "$tag" "$wrapper_commit" "$agent_commit" "$archive_hash") 2>/dev/null; then
  die "Windows proof fixture accepted missing payload records"
fi

expected_assets="$(jq -cn \
  --arg archive "$FINAL_ARCHIVE_NAME" --arg checksum "$FINAL_CHECKSUM_NAME" \
  --arg msi "$FINAL_MSI_NAME" --arg msi_checksum "$FINAL_MSI_CHECKSUM_NAME" \
  --arg archive_digest "sha256:$archive_hash" --arg msi_digest "sha256:$msi_hash" \
  '[{name: $archive, size: 1000, digest: $archive_digest},
    {name: $checksum, size: 94, digest: ("sha256:" + ("4" * 64))},
    {name: $msi, size: 2000, digest: $msi_digest},
    {name: $msi_checksum, size: 94, digest: ("sha256:" + ("7" * 64))}]')"
release_json="$(jq -n --arg tag "$tag" --argjson expected "$expected_assets" \
  '{draft: true, tag_name: $tag, assets: [$expected[] | . + {state: "uploaded"}]}')"
validate_github_release_asset_readback "$release_json" "$tag" true "$expected_assets"
published_json="$(jq '.draft = false' <<<"$release_json")"
validate_github_release_asset_readback "$published_json" "$tag" false "$expected_assets"
null_digest_json="$(jq '.assets[0].digest = null' <<<"$release_json")"
if validate_github_release_asset_readback "$null_digest_json" "$tag" true "$expected_assets"; then
  die "GitHub readback fixture accepted a missing asset digest"
fi
wrong_state_json="$(jq '.assets[1].state = "new"' <<<"$release_json")"
if validate_github_release_asset_readback "$wrong_state_json" "$tag" true "$expected_assets"; then
  die "GitHub readback fixture accepted a non-uploaded asset"
fi
missing_msi_json="$(jq --arg msi "$FINAL_MSI_NAME" '.assets |= map(select(.name != $msi))' <<<"$release_json")"
if validate_github_release_asset_readback "$missing_msi_json" "$tag" true "$expected_assets"; then
  die "GitHub readback fixture accepted a release without the MSI"
fi

publisher="$SCRIPT_DIR/publish-windows-release.sh"
signer="$SCRIPT_DIR/sign-windows-release.sh"
unsigned_preparer="$SCRIPT_DIR/prepare-unsigned-windows-release.ps1"
grep -Fq '$PublishedFileRecords += [pscustomobject][ordered]@{' "$unsigned_preparer" \
  || die "unsigned Windows preparer must emit object records for property-safe provenance aggregation"
grep -Fq 'Add-ReleaseArchiveFile $Archive ([string]$PublishedRecord.path) $SourcePath' "$unsigned_preparer" \
  || die "unsigned Windows preparer must archive normalized relative manifest paths"
grep -Fq 'Add-ReleaseArchiveFile $Archive "release-provenance.json" $ProvenancePath' "$unsigned_preparer" \
  || die "unsigned Windows preparer must include release provenance in the archive"
if grep -Fq 'Compress-Archive -Path' "$unsigned_preparer"; then
  die "unsigned Windows preparer must not use Compress-Archive path globbing for release payloads"
fi
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
