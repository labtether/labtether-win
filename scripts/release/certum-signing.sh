#!/usr/bin/env bash
# Certum signing for sign-windows-release.sh, sourced after
# windows-release-common.sh. Kept apart so test-certum-secret-transport.sh can
# check the real functions.
#
# The Certum key never leaves Certum's SimplySign cloud. The module logs in
# with CERTUM_EMAIL and a one-time code it makes from CERTUM_OTP, and keeps a
# short-lived login token under XDG_RUNTIME_DIR: a private folder inside this
# run's scratch directory, deleted with it.
CERTUM_TIMESTAMP_URL="http://time.certum.pl/"
certum_session_dir=""

read_signing_identity() {
  [[ -n "${CERTUM_EMAIL:-}" && -n "${CERTUM_OTP:-}" ]] || die "CERTUM_EMAIL and CERTUM_OTP are required for signing"
  [[ -n "${SSIGN_PKCS11_MODULE:-}" && -f "$SSIGN_PKCS11_MODULE" && ! -L "$SSIGN_PKCS11_MODULE" ]] \
    || die "SSIGN_PKCS11_MODULE must name the reviewed ssign PKCS#11 module"
  [[ "${SSIGN_PKCS11_MODULE_SHA256:-}" =~ ^[0-9a-f]{64}$ ]] || die "SSIGN_PKCS11_MODULE_SHA256 is required"
  [[ "$(sha256_file "$SSIGN_PKCS11_MODULE")" == "$SSIGN_PKCS11_MODULE_SHA256" ]] \
    || die "the PKCS#11 module differs from the build that was checked"
  [[ -n "${CERTUM_CHAIN_PEM:-}" && -f "$CERTUM_CHAIN_PEM" && ! -L "$CERTUM_CHAIN_PEM" ]] \
    || die "CERTUM_CHAIN_PEM must name the Certum Code Signing 2021 CA certificate"
  if [[ -n "${OSSL_PKCS11_ENGINE:-}" ]]; then
    [[ -f "$OSSL_PKCS11_ENGINE" && ! -L "$OSSL_PKCS11_ENGINE" ]] || die "OSSL_PKCS11_ENGINE is unavailable"
  fi
  certum_session_dir="${scratch_root:?the caller sets scratch_root}/certum-session"
  mkdir -m 700 -- "$certum_session_dir"
  assert_private_directory "$certum_session_dir"
}

forget_signing_identity() {
  if [[ -n "${certum_session_dir:-}" && -d "$certum_session_dir" ]]; then
    rm -rf -- "$certum_session_dir"
  fi
  certum_session_dir=""
}

# Runs osslsigncode with only what it and the module need. Every other
# variable the caller holds (tokens, other signing material) is dropped, the
# module's logging is off, and the seed travels in the environment only.
certum_osslsigncode() {
  (
    local name
    while IFS= read -r name; do
      case "$name" in
        PATH|HOME|LANG|LC_ALL|TMPDIR|CERTUM_EMAIL|CERTUM_OTP) ;;
        *) unset "$name" 2>/dev/null || true ;;
      esac
    done < <(compgen -e)
    export XDG_RUNTIME_DIR="$certum_session_dir" RUST_LOG=off
    exec osslsigncode "$@"
  )
}

# Writes a signed copy of a PE or MSI file and checks the signature.
authenticode_sign() {
  local input_path="$1"
  local output_path="$2"
  local -a engine_args=()
  [[ -n "$certum_session_dir" ]] || die "signing identity is not ready"
  if [[ -n "${OSSL_PKCS11_ENGINE:-}" ]]; then
    engine_args=(-pkcs11engine "$OSSL_PKCS11_ENGINE")
  fi
  # Output stays out of the log: a failed login can print login URLs.
  if ! certum_osslsigncode sign \
      ${engine_args[@]+"${engine_args[@]}"} \
      -pkcs11module "$SSIGN_PKCS11_MODULE" \
      -pkcs11cert "pkcs11:type=cert" \
      -key "pkcs11:type=private" \
      -ac "$CERTUM_CHAIN_PEM" \
      -h sha256 \
      -ts "$CERTUM_TIMESTAMP_URL" \
      -n "LabTether Agent" \
      -in "$input_path" \
      -out "$output_path" >/dev/null 2>&1; then
    die "Authenticode signing failed"
  fi
  osslsigncode verify -in "$output_path" >/dev/null 2>&1 || die "Authenticode verification failed after signing"
}
