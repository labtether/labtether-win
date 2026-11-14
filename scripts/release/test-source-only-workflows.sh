#!/usr/bin/env bash
# Hosted workflows may build and test, but never sign, read secrets, attest,
# or create or upload GitHub releases. Two exceptions are allowed:
# - unsigned-release-build.yml, started by hand only, may run the tagged
#   prepare-unsigned-windows-release.ps1 and build-msi.ps1 and keep the
#   unsigned ZIP and installer as a one-day run artifact.
# - signed-release.yml, started by hand only, may sign with the Certum cloud
#   certificate (only its two sign jobs, in the windows-release-signing
#   environment, with only CERTUM_EMAIL and CERTUM_OTP) and leave a draft
#   GitHub release. It never publishes.
# After checking the real workflows, a self-test checks that the policy
# refuses each way either exception could be widened.
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
UNSIGNED_BUILD="unsigned-release-build.yml"
SIGNED_RELEASE="signed-release.yml"
SIGNING_ENVIRONMENT="windows-release-signing"
# The ssign commit whose code was reviewed (docs/releasing.md). Moving to
# another commit needs a new review and a change here.
REVIEWED_SSIGN_COMMIT="367bf7d3da14d5041d5e47f855ae2397b8028e33"
FORBIDDEN='(secrets(\.|\[)|contents:[[:space:]]*write|id-token:[[:space:]]*write|attestations:[[:space:]]*write|artifact-metadata:[[:space:]]*write|packages:[[:space:]]*write|actions/upload-release-asset|actions/attest|softprops/action-gh-release|gh[[:space:]]+release|signtool|osslsigncode|Import-PfxCertificate|scripts/release/((sign|publish|verify-signed)-windows-release|sign_windows_esigner|windows-esigner-host))'

fail() {
  printf 'error: %s\n' "$1" >&2
  exit 1
}

check_unsigned_build() {
  local workflow="$1"
  # Started by hand only: no other trigger may run it.
  grep -Eq '^on:[[:space:]]*$' "$workflow" || fail "$UNSIGNED_BUILD must list its trigger under on:"
  [[ "$(grep -Ec '^  [a-z_]+:' <(awk '/^on:/{f=1; next} /^[^ ]/{f=0} f' "$workflow"))" == 1 ]] ||
    fail "$UNSIGNED_BUILD must have exactly one trigger"
  grep -Eq '^  workflow_dispatch:[[:space:]]*$' "$workflow" || fail "$UNSIGNED_BUILD must be started by workflow_dispatch only"
  for input in tag agent_tag confirm; do
    grep -Eq "^      ${input}:[[:space:]]*$" "$workflow" || fail "$UNSIGNED_BUILD lacks the $input input"
  done
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  grep -Fq '[[ "$CONFIRM" == "$TAG" ]]' "$workflow" || fail "$UNSIGNED_BUILD does not require confirm to repeat the tag"
  # Only read access, and nothing else under permissions.
  [[ "$(awk '/^permissions:/{f=1; next} /^[^ ]/{f=0} f && NF' "$workflow")" =~ ^[[:space:]]+contents:[[:space:]]+read[[:space:]]*$ ]] ||
    fail "$UNSIGNED_BUILD may only have contents: read"
  grep -Eq '^[[:space:]]+permissions:' "$workflow" && fail "$UNSIGNED_BUILD must not widen permissions per job"
  grep -Fq 'scripts\release\prepare-unsigned-windows-release.ps1' "$workflow" || fail "$UNSIGNED_BUILD must run the tagged prepare script"
  # An unsigned release still ships the installer people expect.
  grep -Fq 'wrapper\scripts\build-msi.ps1' "$workflow" || fail "$UNSIGNED_BUILD must build the installer with the tagged build-msi.ps1"
  # Exactly one artifact upload, pinned, kept for one day.
  [[ "$(grep -Ec 'uses:[[:space:]]+actions/upload-artifact@' "$workflow")" == 1 ]] || fail "$UNSIGNED_BUILD must upload exactly one artifact"
  grep -Eq 'uses:[[:space:]]+actions/upload-artifact@[0-9a-f]{40}([[:space:]]|$)' "$workflow" || fail "$UNSIGNED_BUILD must pin upload-artifact to a commit"
  grep -Eq '^[[:space:]]+retention-days:[[:space:]]+1[[:space:]]*$' "$workflow" || fail "$UNSIGNED_BUILD must keep its artifact for one day"
  grep -Eq '^[[:space:]]+timeout-minutes:[[:space:]]+[0-9]+' "$workflow" || fail "$UNSIGNED_BUILD needs a job timeout"
  if grep -E 'uses:' "$workflow" | grep -Evq 'uses:[[:space:]]+[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}([[:space:]]|$)'; then
    fail "$UNSIGNED_BUILD must pin every action to a commit"
  fi
}

# Prints "job<TAB>line" for every line inside each job of a workflow.
job_lines() {
  awk '/^jobs:/{in_jobs=1; next} /^[^ ]/{in_jobs=0} in_jobs && /^  [A-Za-z0-9_-]+:[[:space:]]*$/{job=$1; sub(/:$/, "", job); next} in_jobs && job {print job "\t" $0}' "$1"
}

check_signed_release() {
  local workflow="$1" s="$SIGNED_RELEASE"
  local env_jobs perm_jobs job text upload_count retention_count job_count timeout_count mask_jobs
  # Started by hand only, with the tag typed twice.
  grep -Eq '^on:[[:space:]]*$' "$workflow" || fail "$s must list its trigger under on:"
  [[ "$(grep -Ec '^  [a-z_]+:' <(awk '/^on:/{f=1; next} /^[^ ]/{f=0} f' "$workflow"))" == 1 ]] ||
    fail "$s must have exactly one trigger"
  grep -Eq '^  workflow_dispatch:[[:space:]]*$' "$workflow" || fail "$s must be started by workflow_dispatch only"
  for input in tag agent_tag confirm; do
    grep -Eq "^      ${input}:[[:space:]]*$" "$workflow" || fail "$s lacks the $input input"
  done
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  grep -Fq '[[ "$CONFIRM" == "$TAG" ]]' "$workflow" || fail "$s does not require confirm to repeat the tag"
  [[ "$(awk '/^permissions:/{f=1; next} /^[^ ]/{f=0} f && NF' "$workflow")" =~ ^[[:space:]]+contents:[[:space:]]+read[[:space:]]*$ ]] ||
    fail "$s may only have contents: read at the top"
  # Only the draft job widens permissions, and only to contents: write.
  perm_jobs="$(job_lines "$workflow" | awk -F'\t' '$2 ~ /^    permissions:/{print $1}' | sort -u | tr '\n' ' ')"
  [[ "$perm_jobs" == "draft " ]] || fail "$s may widen permissions only in its draft job"
  [[ "$(job_lines "$workflow" | awk -F'\t' '$1 == "draft" && $2 ~ /^    permissions:/{f=1; next} $1 == "draft" && f && $2 ~ /^      /{print $2; next} {f=0}')" =~ ^[[:space:]]+contents:[[:space:]]+write[[:space:]]*$ ]] ||
    fail "$s draft job may only add contents: write"
  # Secrets only in the two sign jobs, through the signing environment, and
  # only the two Certum values.
  env_jobs="$(job_lines "$workflow" | awk -F'\t' '$2 ~ /^    environment:/{print $1}' | sort | tr '\n' ' ')"
  [[ "$env_jobs" == "sign-archive sign-msi " ]] || fail "$s may use an environment only in sign-archive and sign-msi"
  [[ "$(job_lines "$workflow" | awk -F'\t' '$2 ~ /^    environment:/{print $2}' | sort -u)" =~ ^[[:space:]]+environment:[[:space:]]+${SIGNING_ENVIRONMENT}[[:space:]]*$ ]] ||
    fail "$s sign jobs must use the $SIGNING_ENVIRONMENT environment"
  grep -Eq '^[[:space:]]+environment:' <(awk '/^jobs:/{exit} {print}' "$workflow") && fail "$s must not set an environment for every job"
  while IFS=$'\t' read -r job text; do
    [[ "$job" == sign-archive || "$job" == sign-msi ]] || fail "$s reads a secret outside the sign jobs"
    [[ "$text" =~ secrets\.CERTUM_(EMAIL|OTP)[[:space:]]*\}\} && ! "$text" =~ secrets\.CERTUM_(EMAIL|OTP)[A-Za-z0-9_] ]] || fail "$s reads a secret other than CERTUM_EMAIL or CERTUM_OTP"
    [[ "$(grep -o 'secrets[.[]' <<<"$text" | wc -l | tr -d ' ')" == 1 ]] || fail "$s reads more than one secret on a line"
  done < <(job_lines "$workflow" | grep -E 'secrets[.[]' || true)
  grep -Eq 'secrets[.[]' <(awk '/^jobs:/{exit} {print}' "$workflow") && fail "$s reads a secret outside its jobs"
  # Every sign job masks every form of the seed before it signs.
  mask_jobs="$(job_lines "$workflow" | awk -F'\t' '$2 ~ /::add-mask::/{print $1}' | sort -u | tr '\n' ' ')"
  [[ "$mask_jobs" == "sign-archive sign-msi " ]] || fail "$s must mask the signing seed in each sign job"
  # It signs only with the reviewed Certum route and never publishes.
  grep -Eq "^  SSIGN_COMMIT:[[:space:]]+${REVIEWED_SSIGN_COMMIT}[[:space:]]*$" "$workflow" || fail "$s must build the reviewed ssign commit"
  [[ "$(grep -Ec 'sign-windows-release\.sh' "$workflow")" == 2 ]] || fail "$s must sign only through sign-windows-release.sh, once per sign job"
  grep -Eiq 'signtool|Import-PfxCertificate|pkcs12|WIN_CSC|ESIGNER|SSL_COM|readpass|set -x|ACTIONS_STEP_DEBUG|ACTIONS_RUNNER_DEBUG' "$workflow" &&
    fail "$s contains another signing route or debug logging"
  grep -Eq -- '--confirm-publish|gh[[:space:]]+release|--draft=false|softprops/|upload-release-asset|actions/attest|id-token:|attestations:|packages:' "$workflow" &&
    fail "$s may only create a draft through publish-windows-release.sh"
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  [[ "$(grep -Fc -- '--confirm-draft "$CONFIRM"' "$workflow")" == 1 ]] || fail "$s must create the draft with --confirm-draft"
  # Pinned actions, one-day artifacts, a timeout for every job.
  if grep -E 'uses:' "$workflow" | grep -Evq 'uses:[[:space:]]+[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}([[:space:]]|$)'; then
    fail "$s must pin every action to a commit"
  fi
  upload_count="$(grep -Ec 'uses:[[:space:]]+actions/upload-artifact@' "$workflow")"
  retention_count="$(grep -Ec '^[[:space:]]+retention-days:[[:space:]]+1[[:space:]]*$' "$workflow")"
  [[ "$upload_count" -gt 0 && "$upload_count" == "$retention_count" ]] || fail "$s must keep every artifact for one day"
  [[ "$(grep -Ec '^[[:space:]]+retention-days:' "$workflow")" == "$retention_count" ]] || fail "$s must keep every artifact for one day"
  job_count="$(grep -Ec '^  [A-Za-z0-9_-]+:[[:space:]]*$' <(awk '/^jobs:/{f=1; next} /^[^ ]/{f=0} f' "$workflow"))"
  timeout_count="$(grep -Ec '^    timeout-minutes:[[:space:]]+[0-9]+' "$workflow")"
  [[ "$job_count" == "$timeout_count" ]] || fail "$s needs a timeout for every job"
}

check_workflows() {
  local workflow_root="$1" workflow
  local -a workflows=()
  while IFS= read -r -d '' workflow; do
    workflows+=("$workflow")
  done < <(find "$workflow_root" -maxdepth 1 -type f \( -name '*.yml' -o -name '*.yaml' \) -print0)
  (( ${#workflows[@]} > 0 )) || fail "no workflows were found"

  for workflow in "${workflows[@]}"; do
    grep -Eq '^permissions:[[:space:]]*$' "$workflow" || fail "workflow lacks an explicit permissions block"
    grep -Eq '^[[:space:]]+contents:[[:space:]]+read([[:space:]]|$)' "$workflow" || fail "workflow lacks read-only contents permission"
    if [[ "$(basename "$workflow")" == "$SIGNED_RELEASE" ]]; then
      check_signed_release "$workflow"
    elif grep -Eiq "$FORBIDDEN" "$workflow"; then
      fail "hosted workflow contains a signing, secret, attestation, or release-publication capability"
    fi
    if [[ "$(basename "$workflow")" == "$SIGNED_RELEASE" ]]; then
      :
    elif [[ "$(basename "$workflow")" == "$UNSIGNED_BUILD" ]]; then
      check_unsigned_build "$workflow"
    elif grep -Eiq 'actions/upload-artifact|prepare-unsigned-windows-release' "$workflow"; then
      fail "only $UNSIGNED_BUILD may build or keep a release archive"
    fi
    checkout_count="$(grep -Ec 'uses:[[:space:]]+actions/checkout@' "$workflow" || true)"
    credential_off_count="$(grep -Ec 'persist-credentials:[[:space:]]+false' "$workflow" || true)"
    (( credential_off_count >= checkout_count )) || fail "workflow checkout does not consistently disable persisted credentials"
  done

  release_workflow="$workflow_root/release.yml"
  [[ -f "$release_workflow" ]] || fail "release source-verification workflow is missing"
  grep -Eq '^name:[[:space:]]+Release source verification[[:space:]]*$' "$release_workflow" || fail "release workflow is not explicitly source verification"
  grep -Fq 'Signing happens only in signed-release.yml, and publishing stays a maintainer step.' "$release_workflow" || fail "release workflow does not state the release boundary"
}

# Each case changes one copy of the real workflows; the policy must refuse it.
# perl does the edits, with the text passed in the environment, so the
# self-test runs the same on macOS and in Git Bash on Windows.
UPLOAD_STEP='      - uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2'
CASES_REFUSED=0

expect_refused() {
  local case_name="$1" target="$2" mode="$3"
  rm -rf -- "${fixture:?}/w"
  mkdir -p -- "$fixture/w"
  local copy
  # A Windows checkout can have CRLF line endings; the edits work on LF copies.
  for copy in "$REPO_ROOT"/.github/workflows/*.yml; do
    perl -pe 's/\r$//' "$copy" > "$fixture/w/$(basename "$copy")"
  done
  cp -- "$fixture/w/$target" "$fixture/before"
  case "$mode" in
    append) LINE="$4" perl -0pe 's/\z/\n$ENV{LINE}\n/' "$fixture/w/$target" > "$fixture/w/edited" ;;
    sub) FROM="$4" TO="$5" perl -0pe 's/$ENV{FROM}/$ENV{TO}/m' "$fixture/w/$target" > "$fixture/w/edited" ;;
  esac
  mv -- "$fixture/w/edited" "$fixture/w/$target"
  if cmp -s -- "$fixture/before" "$fixture/w/$target"; then
    fail "self-test case $case_name did not change $target"
  fi
  if (check_workflows "$fixture/w") 2>/dev/null; then
    fail "workflow policy accepted the $case_name change"
  fi
  CASES_REFUSED=$((CASES_REFUSED + 1))
}

self_test() {
  local u="$UNSIGNED_BUILD" nl=$'\n'
  fixture="$(mktemp -d "${TMPDIR:-/tmp}/labtether-workflow-policy.XXXXXX")"
  trap 'rm -rf -- "$fixture"' EXIT
  expect_refused upload-in-ci ci.yml append "$UPLOAD_STEP"
  expect_refused push-trigger "$u" sub '^  workflow_dispatch:$' "  push:${nl}    tags: [\"v*\"]${nl}  workflow_dispatch:"
  expect_refused longer-retention "$u" sub 'retention-days: 1$' 'retention-days: 7'
  expect_refused unpinned-upload "$u" sub 'upload-artifact@[0-9a-f]{40}' 'upload-artifact@v7'
  expect_refused second-upload "$u" append "$UPLOAD_STEP"
  expect_refused release-upload "$u" append '      - run: gh release upload v0.0.0 x.zip'
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  expect_refused secret-read "$u" append '      - run: echo ${{ secrets.TOKEN }}'
  expect_refused write-permission "$u" sub '^  contents: read$' '  contents: write'
  expect_refused extra-permission "$u" sub '^  contents: read$' "  contents: read${nl}  actions: write"
  expect_refused job-permission "$u" sub '^    timeout-minutes:' "    permissions:${nl}      actions: read${nl}    timeout-minutes:"
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  expect_refused no-confirm "$u" sub '^.*"\$CONFIRM" == "\$TAG".*\n' ''
  expect_refused signing-script "$u" append '      - run: bash scripts/release/sign-windows-release.sh'
  expect_refused no-installer "$u" sub 'wrapper\\scripts\\build-msi\.ps1' 'wrapper\\scripts\\none.ps1'
  local r="$SIGNED_RELEASE"
  expect_refused signed-push-trigger "$r" sub '^  workflow_dispatch:$' "  push:${nl}    tags: [\"v*\"]${nl}  workflow_dispatch:"
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  expect_refused signed-publish "$r" sub '--confirm-draft "\$CONFIRM"' '--confirm-publish "$CONFIRM"'
  expect_refused signed-gh-release "$r" append '      - run: gh release edit v0.0.0 --draft=false'
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  expect_refused signed-other-secret "$r" sub 'secrets\.CERTUM_EMAIL \}\}' 'secrets.GITHUB_PAT }}'
  # shellcheck disable=SC2016 # literal workflow text, not shell expansion
  expect_refused signed-secret-in-draft "$r" append '          SOMETHING: ${{ secrets.CERTUM_OTP }}'
  expect_refused signed-no-environment "$r" sub '^    environment: windows-release-signing\n' ''
  expect_refused signed-unreviewed-ssign "$r" sub 'SSIGN_COMMIT: 367bf7d3da14d5041d5e47f855ae2397b8028e33' 'SSIGN_COMMIT: 0000000000000000000000000000000000000000'
  expect_refused signed-no-mask "$r" sub 'print\(f"::add-mask::\{p\}"\)' 'print(p)'
  expect_refused signed-longer-retention "$r" sub 'retention-days: 1$' 'retention-days: 7'
  expect_refused signed-unpinned "$r" sub 'download-artifact@[0-9a-f]{40}' 'download-artifact@v8'
  expect_refused signed-write-everywhere "$r" sub '^  contents: read$' '  contents: write'
  expect_refused signed-debug "$r" append '      - run: set -x'
  expect_refused signed-signtool "$r" append '      - run: signtool sign /f x.pfx app.exe'
  printf 'Workflow policy refused all %d widening cases.\n' "$CASES_REFUSED"
}

check_workflows "$REPO_ROOT/.github/workflows"
printf 'Hosted workflow source-only policy passed.\n'
self_test
