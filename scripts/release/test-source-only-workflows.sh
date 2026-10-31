#!/usr/bin/env bash
# Hosted workflows may build and test, but never sign, read secrets, attest,
# or create or upload GitHub releases. One exception is allowed:
# unsigned-release-build.yml, started by hand only, may run the tagged
# prepare-unsigned-windows-release.ps1 and keep its unsigned ZIP as a one-day
# run artifact. After checking the real workflows, a self-test checks that the
# policy refuses each way that exception could be widened.
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
UNSIGNED_BUILD="unsigned-release-build.yml"
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
  # Exactly one artifact upload, pinned, kept for one day.
  [[ "$(grep -Ec 'uses:[[:space:]]+actions/upload-artifact@' "$workflow")" == 1 ]] || fail "$UNSIGNED_BUILD must upload exactly one artifact"
  grep -Eq 'uses:[[:space:]]+actions/upload-artifact@[0-9a-f]{40}([[:space:]]|$)' "$workflow" || fail "$UNSIGNED_BUILD must pin upload-artifact to a commit"
  grep -Eq '^[[:space:]]+retention-days:[[:space:]]+1[[:space:]]*$' "$workflow" || fail "$UNSIGNED_BUILD must keep its artifact for one day"
  grep -Eq '^[[:space:]]+timeout-minutes:[[:space:]]+[0-9]+' "$workflow" || fail "$UNSIGNED_BUILD needs a job timeout"
  if grep -E 'uses:' "$workflow" | grep -Evq 'uses:[[:space:]]+[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}([[:space:]]|$)'; then
    fail "$UNSIGNED_BUILD must pin every action to a commit"
  fi
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
    if grep -Eiq "$FORBIDDEN" "$workflow"; then
      fail "hosted workflow contains a signing, secret, attestation, or release-publication capability"
    fi
    if [[ "$(basename "$workflow")" == "$UNSIGNED_BUILD" ]]; then
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
  grep -Fq 'Signing and publication require the local release scripts.' "$release_workflow" || fail "release workflow does not assert the local-only release boundary"
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
  printf 'Workflow policy refused all %d widening cases.\n' "$CASES_REFUSED"
}

check_workflows "$REPO_ROOT/.github/workflows"
printf 'Hosted workflow source-only policy passed.\n'
self_test
