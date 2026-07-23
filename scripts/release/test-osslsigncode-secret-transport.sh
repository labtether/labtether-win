#!/usr/bin/env bash
set -Eeuo pipefail
set +x
umask 077

die() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

[[ $# == 1 ]] || die "usage: test-osslsigncode-secret-transport.sh UNSIGNED_PE"
unsigned_pe="$1"
[[ -f "$unsigned_pe" && ! -L "$unsigned_pe" ]] || die "unsigned PE fixture is unavailable"
for command_name in openssl osslsigncode ps; do
  command -v "$command_name" >/dev/null 2>&1 || die "missing required fixture command: $command_name"
done
if osslsigncode verify -in "$unsigned_pe" >/dev/null 2>&1; then
  die "fixture input must be an unsigned PE"
fi

fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/labtether-fd-signing-fixture.XXXXXX")"
password=""
cleanup() {
  unset password
  exec 9<&- 2>/dev/null || true
  rm -rf -- "$fixture_root"
}
trap cleanup EXIT

config="$fixture_root/fixture.cnf"
key="$fixture_root/fixture.key"
certificate="$fixture_root/fixture.crt"
bundle="$fixture_root/fixture.p12"
signed_pe="$fixture_root/signed.exe"
sign_log="$fixture_root/sign.log"
process_snapshot="$fixture_root/process.snapshot"
environment_snapshot="$fixture_root/environment.snapshot"
signer_wrapper="$fixture_root/record-signer-process.sh"

printf '%s\n' \
  '[req]' \
  'distinguished_name = subject' \
  'x509_extensions = extensions' \
  'prompt = no' \
  '[subject]' \
  'CN = LabTether Throwaway Signing Fixture' \
  '[extensions]' \
  'keyUsage = critical,digitalSignature' \
  'extendedKeyUsage = codeSigning' > "$config"
openssl req -x509 -newkey rsa:2048 -nodes -days 1 \
  -config "$config" -keyout "$key" -out "$certificate" >/dev/null 2>&1
password="$(openssl rand -hex 32)"
[[ -n "$password" ]] || die "unable to generate a throwaway fixture password"
printf '%s\n' "$password" | openssl pkcs12 -export \
  -inkey "$key" -in "$certificate" -out "$bundle" -passout stdin >/dev/null 2>&1
# These are literal lines for the generated wrapper; expansion belongs to the
# wrapper at execution time, not to this fixture generator.
# shellcheck disable=SC2016
printf '%s\n' \
  '#!/usr/bin/env bash' \
  'set -Eeuo pipefail' \
  'real_signer="$1"' \
  'process_snapshot="$2"' \
  'environment_snapshot="$3"' \
  'shift 3' \
  'ps eww -p $$ -o command= > "$process_snapshot"' \
  'env -0 > "$environment_snapshot"' \
  'exec "$real_signer" "$@"' > "$signer_wrapper"
chmod 0700 "$signer_wrapper"

{ exec 9<"$bundle"; } 2>/dev/null || die "unable to open throwaway fixture bundle"
if ! printf '%s\n' "$password" | "$signer_wrapper" "$(command -v osslsigncode)" "$process_snapshot" "$environment_snapshot" sign \
  -pkcs12 /dev/fd/9 \
  -readpass - \
  -h sha256 \
  -ts "http://timestamp.digicert.com" \
  -n "LabTether Signing Transport Fixture" \
  -in "$unsigned_pe" \
  -out "$signed_pe" > "$sign_log" 2>&1; then
  die "throwaway Authenticode signing failed"
fi
exec 9<&-

if grep -F -q -- "$password" "$sign_log" "$process_snapshot" "$environment_snapshot"; then
  die "throwaway password appeared in signer output, argv, or environment"
fi
if grep -F -q -- "$bundle" "$process_snapshot"; then
  die "throwaway bundle path appeared in the signer process arguments"
fi
verify_output="$(osslsigncode verify -verbose -CAfile "$certificate" -in "$signed_pe" 2>&1)" || die "throwaway signed PE did not verify"
[[ "$(printf '%s\n' "$verify_output" | grep -c '^Signature Index:')" == "1" ]] || die "throwaway PE does not contain exactly one signature"
printf '%s\n' "$verify_output" | grep -q '^Timestamp Server Signature verification: ok$' || die "throwaway PE lacks a valid trusted timestamp"
printf '%s\n' "$verify_output" | grep -q '^Signature verification: ok$' || die "throwaway PE signature verification failed"
printf '%s\n' "$verify_output" | grep -q '^Number of verified signatures: 1$' || die "throwaway PE signature-count verification failed"

unset password
printf 'osslsigncode file-descriptor and password-stdin fixture passed.\n'
