#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
workflow_root="$REPO_ROOT/.github/workflows"

fail() {
  printf 'error: %s\n' "$1" >&2
  exit 1
}

mapfile_supported=false
if (( BASH_VERSINFO[0] >= 4 )); then
  mapfile_supported=true
fi
workflows=()
if $mapfile_supported; then
  mapfile -d '' workflows < <(find "$workflow_root" -maxdepth 1 -type f \( -name '*.yml' -o -name '*.yaml' \) -print0)
else
  while IFS= read -r workflow; do
    workflows+=("$workflow")
  done < <(find "$workflow_root" -maxdepth 1 -type f \( -name '*.yml' -o -name '*.yaml' \) -print)
fi
(( ${#workflows[@]} > 0 )) || fail "no workflows were found"

for workflow in "${workflows[@]}"; do
  grep -Eq '^permissions:[[:space:]]*$' "$workflow" || fail "workflow lacks an explicit permissions block"
  grep -Eq '^[[:space:]]+contents:[[:space:]]+read([[:space:]]|$)' "$workflow" || fail "workflow lacks read-only contents permission"
  if grep -Eiq \
    '(secrets(\.|\[)|contents:[[:space:]]*write|id-token:[[:space:]]*write|attestations:[[:space:]]*write|artifact-metadata:[[:space:]]*write|packages:[[:space:]]*write|actions/upload-(artifact|release-asset)|actions/attest|softprops/action-gh-release|gh[[:space:]]+release|signtool|osslsigncode|Import-PfxCertificate|scripts/release/(sign|publish|prepare|verify)-windows-release)' \
    "$workflow"; then
    fail "hosted workflow contains a signing, secret, attestation, artifact-upload, or release-publication capability"
  fi
  checkout_count="$(grep -Ec 'uses:[[:space:]]+actions/checkout@' "$workflow" || true)"
  credential_off_count="$(grep -Ec 'persist-credentials:[[:space:]]+false' "$workflow" || true)"
  (( credential_off_count >= checkout_count )) || fail "workflow checkout does not consistently disable persisted credentials"
done

release_workflow="$workflow_root/release.yml"
[[ -f "$release_workflow" ]] || fail "release source-verification workflow is missing"
grep -Eq '^name:[[:space:]]+Release source verification[[:space:]]*$' "$release_workflow" || fail "release workflow is not explicitly source verification"
grep -Fq 'Signing and publication require the local release scripts.' "$release_workflow" || fail "release workflow does not assert the local-only release boundary"

printf 'Hosted workflow source-only policy passed.\n'
