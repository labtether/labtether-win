#!/usr/bin/env bash
# Runs the release tracked-source policy on a checkout without building or
# signing. The release scripts refuse a tag whose tracked files hold a
# certificate, a key or a local secret path. CI and the release dry run call
# this so a pull request fails first, before any tag exists.
#
# Usage: check-tracked-source-policy.sh [checkout ...]   (default: this repo)
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/release/windows-release-common.sh
source "$SCRIPT_DIR/windows-release-common.sh"

[[ $# -gt 0 ]] || set -- "$(cd "$SCRIPT_DIR/../.." && pwd -P)"
for checkout in "$@"; do
  checkout="$(cd "$checkout" 2>/dev/null && pwd -P)" || die "tracked-source check: not a directory"
  assert_tracked_source_policy "$checkout"
  printf 'Tracked source policy passed: %s\n' "$(basename "$checkout")"
done
