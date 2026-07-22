#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
AGENT_REPO="${LABTETHER_AGENT_REPO:-$(cd "${REPO_ROOT}/.." && pwd)/labtether-agent}"
OUTPUT_DIR="${REPO_ROOT}/src/LabTetherAgent/Assets"
ARCH="amd64"
VERSION="${LABTETHER_AGENT_VERSION:-}"

usage() {
  cat <<'USAGE'
Usage: scripts/build-bundled-agent.sh [options]

Build the matching Go agent core from source for the native Windows app.

Options:
  --agent-repo PATH  Path to the labtether-agent checkout
  --arch amd64|arm64 Target Windows architecture (default: amd64)
  --output-dir PATH  Output directory (default: src/LabTetherAgent/Assets)
  --version VERSION  Version embedded in the child and AGENT_VERSION
  -h, --help         Show this help

Environment:
  LABTETHER_AGENT_REPO
  LABTETHER_AGENT_VERSION
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --agent-repo)
      AGENT_REPO="${2:?--agent-repo requires a value}"
      shift 2
      ;;
    --arch)
      ARCH="${2:?--arch requires a value}"
      shift 2
      ;;
    --output-dir)
      OUTPUT_DIR="${2:?--output-dir requires a value}"
      shift 2
      ;;
    --version)
      VERSION="${2:?--version requires a value}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

case "${ARCH}" in
  amd64|arm64) ;;
  *)
    echo "unsupported Windows architecture: ${ARCH}" >&2
    exit 2
    ;;
esac

command -v go >/dev/null 2>&1 || {
  echo "missing required command: go" >&2
  exit 1
}

if [[ ! -f "${AGENT_REPO}/go.mod" || ! -d "${AGENT_REPO}/cmd/labtether-agent" ]]; then
  echo "labtether-agent checkout not found at ${AGENT_REPO}" >&2
  exit 1
fi

if [[ -z "${VERSION}" ]]; then
  VERSION="$(git -C "${AGENT_REPO}" describe --tags --always --dirty 2>/dev/null || printf dev)"
fi
if [[ -z "${VERSION}" || "${VERSION}" =~ [[:space:]] ]]; then
  echo "agent version must be non-empty and contain no whitespace" >&2
  exit 1
fi

mkdir -p "${OUTPUT_DIR}"
OUTPUT_DIR="$(cd "${OUTPUT_DIR}" && pwd)"
TEMP_DIR="$(mktemp -d "${OUTPUT_DIR}/.labtether-agent-build.XXXXXX")"
cleanup() {
  rm -rf "${TEMP_DIR}"
}
trap cleanup EXIT

(
  cd "${AGENT_REPO}"
  CGO_ENABLED=0 GOOS=windows GOARCH="${ARCH}" \
    go build -trimpath \
    -ldflags="-s -w -X main.version=${VERSION}" \
    -o "${TEMP_DIR}/labtether-agent.exe" \
    ./cmd/labtether-agent
)

[[ -s "${TEMP_DIR}/labtether-agent.exe" ]] || {
  echo "Go child build did not produce a binary" >&2
  exit 1
}

printf '%s\n' "${VERSION}" > "${TEMP_DIR}/AGENT_VERSION"
mv "${TEMP_DIR}/labtether-agent.exe" "${OUTPUT_DIR}/labtether-agent.exe"
mv "${TEMP_DIR}/AGENT_VERSION" "${OUTPUT_DIR}/AGENT_VERSION"
printf 'Built %s (%s, version %s)\n' "${OUTPUT_DIR}/labtether-agent.exe" "${ARCH}" "${VERSION}"
