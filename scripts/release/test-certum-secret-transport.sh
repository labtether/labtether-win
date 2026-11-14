#!/usr/bin/env bash
# Runs the real Certum signing functions (certum-signing.sh) against a fake
# osslsigncode that records its arguments and environment. Signing logs are
# public on the hosted runner, so this guards that:
# - the SimplySign seed never reaches a command line,
# - no other secret the job holds reaches the signer,
# - the module's logging is off and its login token lives in, and is removed
#   with, a private folder of this run,
# - a module other than the checked build is refused before anything signs.
set -Eeuo pipefail
set +x
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/release/windows-release-common.sh
source "$SCRIPT_DIR/windows-release-common.sh"
# shellcheck source=scripts/release/certum-signing.sh
source "$SCRIPT_DIR/certum-signing.sh"

fixture="$(mktemp -d "${TMPDIR:-/tmp}/labtether-certum-transport.XXXXXX")"
fixture="$(cd "$fixture" && pwd -P)"
trap 'rm -rf -- "$fixture"' EXIT
chmod 700 "$fixture"

seed_value="JBSWY3DPEHPK3PXPFAKESEEDVALUE" # gitleaks:allow (fake test seed)
mkdir -m 700 "$fixture/bin" "$fixture/record"
cat > "$fixture/bin/osslsigncode" <<'FAKE'
#!/usr/bin/env bash
set -Eeuo pipefail
# The scrubber keeps TMPDIR, so the test points it at the record folder.
record="${TMPDIR:?}"
if [[ "$1" == "verify" ]]; then
  grep -q "SIGNED-BY-FAKE" "${@: -1}"
  exit $?
fi
printf '%s\n' "$@" > "$record/argv"
env > "$record/env"
mkdir -p "$XDG_RUNTIME_DIR/ssign"
printf 'token' > "$XDG_RUNTIME_DIR/ssign/session.json"
in="" out=""
while [[ $# -gt 0 ]]; do
  case "$1" in -in) in="$2"; shift 2 ;; -out) out="$2"; shift 2 ;; *) shift ;; esac
done
{ cat "$in"; printf 'SIGNED-BY-FAKE'; } > "$out"
FAKE
chmod 700 "$fixture/bin/osslsigncode"
printf 'module' > "$fixture/libssign_pkcs11.so"
printf 'chain' > "$fixture/chain.pem"
printf 'MZ' > "$fixture/in.exe"

run_signing() {
  (
    export PATH="$fixture/bin:$PATH"
    export CERTUM_EMAIL="signer@example.test"
    export CERTUM_OTP="otpauth://totp/Certum:x?secret=${seed_value}&issuer=Certum&algorithm=SHA256"
    export SSIGN_PKCS11_MODULE="$fixture/libssign_pkcs11.so"
    export SSIGN_PKCS11_MODULE_SHA256="${1:-$(sha256_file "$fixture/libssign_pkcs11.so")}"
    export CERTUM_CHAIN_PEM="$fixture/chain.pem"
    export GH_TOKEN="gh-token-must-not-reach-the-signer"
    export CERTUM_TOKEN="123456"
    export OTHER_SIGNING_SECRET="other-secret-must-not-reach-the-signer"
    export TMPDIR="$fixture/record"
    scratch_root="$fixture/scratch"
    mkdir -p "$scratch_root" && chmod 700 "$scratch_root"
    read_signing_identity
    authenticode_sign "$fixture/in.exe" "$fixture/out.exe"
    [[ -f "$certum_session_dir/ssign/session.json" ]] || die "the login token was not kept in this run's folder"
    forget_signing_identity
    [[ ! -e "$fixture/scratch/certum-session" ]] || die "the login token folder outlived the signing"
  )
}

run_signing
grep -q "SIGNED-BY-FAKE" "$fixture/out.exe" || die "nothing was signed"
grep -Fq -- "$seed_value" "$fixture/record/argv" && die "the seed reached the signer's command line"
grep -Fq -- "otpauth://" "$fixture/record/argv" && die "the seed reached the signer's command line"
grep -Fq "CERTUM_OTP=otpauth://" "$fixture/record/env" || die "the signer did not get the seed in its environment"
grep -Fq "CERTUM_EMAIL=signer@example.test" "$fixture/record/env" || die "the signer did not get the email"
for leaked in GH_TOKEN CERTUM_TOKEN OTHER_SIGNING_SECRET SSIGN_PKCS11_MODULE_SHA256; do
  grep -q "^${leaked}=" "$fixture/record/env" && die "$leaked reached the signer"
done
grep -qx "RUST_LOG=off" "$fixture/record/env" || die "the module's logging was not switched off"
grep -qx "XDG_RUNTIME_DIR=$fixture/scratch/certum-session" "$fixture/record/env" || die "the login token folder is not private to this run"
grep -qx -- "-pkcs11module" "$fixture/record/argv" || die "the signer was not given the PKCS#11 module"
grep -qx -- "http://time.certum.pl/" "$fixture/record/argv" || die "the signature is not timestamped by Certum"

# A module that is not the checked build is refused before anything signs.
rm -f "$fixture/record/argv"
if (run_signing "0000000000000000000000000000000000000000000000000000000000000000") 2>/dev/null; then
  die "a module with another checksum was used"
fi
[[ ! -e "$fixture/record/argv" ]] || die "the signer ran with an unchecked module"

printf 'Certum signing secret transport passed.\n'
