#!/usr/bin/env bash
set -Eeuo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/release/windows-release-common.sh
source "${SCRIPT_DIR}/windows-release-common.sh"
source_policy_allows() { (assert_tracked_source_policy "$1"); }

fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/labtether-guide-policy.XXXXXX")"
trap 'rm -rf -- "${fixture_root}"' EXIT
fixture_index=0

make_fixture() {
  fixture_index=$((fixture_index + 1))
  repo="${fixture_root}/${fixture_index}"
  mkdir -p "${repo}"
  git -C "${repo}" init -q
  git -C "${repo}" config core.autocrlf false
  git -C "${repo}" config core.symlinks true
  printf 'Shared project instructions\n' > "${repo}/AGENTS.md"
  printf 'ordinary fixture\n' > "${repo}/ordinary.txt"
  ln -s AGENTS.md "${repo}/CLAUDE.md"
  git -C "${repo}" add -- AGENTS.md CLAUDE.md ordinary.txt
}

expect_rejected() {
  local label="$1"
  if source_policy_allows "${repo}" >/dev/null 2>&1; then
    printf 'source policy accepted unsafe guide fixture: %s\n' "${label}" >&2
    exit 1
  fi
}

make_fixture
source_policy_allows "${repo}"

make_fixture
rm -- "${repo}/CLAUDE.md"
ln -s ordinary.txt "${repo}/CLAUDE.md"
expect_rejected 'changed local target'
git -C "${repo}" add -- CLAUDE.md
expect_rejected 'changed indexed target'

make_fixture
printf 'dummy fixture only\n' > "${repo}/dummy.key"
rm -- "${repo}/CLAUDE.md"
ln -s dummy.key "${repo}/CLAUDE.md"
git -C "${repo}" add -- CLAUDE.md
expect_rejected 'secret-like target in disposable fixture'

make_fixture
printf 'external fixture only\n' > "${fixture_root}/outside.txt"
rm -- "${repo}/CLAUDE.md"
ln -s "${fixture_root}/outside.txt" "${repo}/CLAUDE.md"
git -C "${repo}" add -- CLAUDE.md
expect_rejected 'external target'

make_fixture
rm -- "${repo}/AGENTS.md"
expect_rejected 'missing guide target'

make_fixture
rm -- "${repo}/AGENTS.md"
ln -s ordinary.txt "${repo}/AGENTS.md"
expect_rejected 'symlinked guide target'

make_fixture
printf 'altered guide\n' >> "${repo}/AGENTS.md"
expect_rejected 'modified target bytes'

make_fixture
git -C "${repo}" rm --cached -q -- AGENTS.md
expect_rejected 'untracked guide target'

make_fixture
rm -- "${repo}/CLAUDE.md"
printf AGENTS.md > "${repo}/CLAUDE.md"
expect_rejected 'plain file instead of local symlink'

make_fixture
ln -s ordinary.txt "${repo}/other-link"
git -C "${repo}" add -- other-link
expect_rejected 'unrelated tracked link'

make_fixture
mkdir "${repo}/nested"
ln -s ../AGENTS.md "${repo}/nested/CLAUDE.md"
git -C "${repo}" add -- nested/CLAUDE.md
expect_rejected 'nested guide link'

make_fixture
newline_hash="$(printf 'AGENTS.md\n' | git -C "${repo}" hash-object -w --stdin)"
git -C "${repo}" update-index --cacheinfo "120000,${newline_hash},CLAUDE.md"
expect_rejected 'non-exact indexed link bytes'

make_fixture
link_hash="$(printf AGENTS.md | git -C "${repo}" hash-object -w --stdin)"
git -C "${repo}" update-index --cacheinfo "120000,${link_hash},AGENTS.md"
expect_rejected 'non-regular target in index'

printf 'Canonical guide link policy fixtures passed.\n'
