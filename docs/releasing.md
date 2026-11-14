# Releasing the Windows app

This is for maintainers. To install the app, see the [README](../README.md).

Windows releases are signed by Watari Labs Pty Ltd with a Certum Standard
Code Signing certificate whose key stays in Certum's SimplySign cloud. The
usual way to release is the [signed release workflow](#signed-release-workflow)
below, started by hand; it leaves a draft release that a maintainer publishes.
The same seven steps can also run by hand, below. The SSL.com eSigner
[manual flow](../scripts/release/README-esigner.md) stays as a fallback until a
Certum-signed release has shipped.

The tag-triggered GitHub workflow (`release.yml`) verifies clean matching
wrapper and agent tags, tests, publishes, builds and checks the MSI, and
validates the payload. It has read-only permissions and never signs.

## Signed release workflow

Start `.github/workflows/signed-release.yml` by hand with `tag`, `agent_tag`
(the same tag) and `confirm` (the tag again). It runs steps 1 to 6 below on
GitHub's machines: Windows builds and checks, Linux signs.

- `signer` builds the ssign PKCS#11 module from the reviewed ssign commit
  (`SSIGN_COMMIT`, review notes in the MetaTana repo's
  `docs/reference/windows-code-signing.md`) with no secrets in the job.
- `sign-archive` and `sign-msi` are the only jobs in the
  `windows-release-signing` environment, whose secrets `CERTUM_EMAIL` and
  `CERTUM_OTP` (the SimplySign seed, the whole `otpauth://` text) only those
  two jobs can read. They mask every form of the seed first, check the
  module's SHA-256 against the `signer` job, and run `sign-windows-release.sh`.
- `draft` uploads exactly the four checked assets to a draft release. Publish
  it on GitHub after looking at it.

The logs are public. `certum-signing.sh` gives `osslsigncode` and the module
only `PATH`, the locale, `TMPDIR`, `CERTUM_EMAIL` and `CERTUM_OTP`, with the
module's logging off, never prints signer output (a failed login can print
login URLs), and deletes the module's login token with the run's scratch
folder. `test-certum-secret-transport.sh` checks this on every pull request,
and `test-source-only-workflows.sh` refuses any wider version of the workflow.

Certum allows 5,000 signatures a month across Watari Labs' products; a
release uses four. Each one-time code works once, so two signing jobs that log
in inside the same 30 seconds collide; run releases one at a time.

## Release steps

1. On Windows, run `scripts/release/prepare-unsigned-windows-release.ps1` from
   clean matching tagged wrapper and agent checkouts. It builds into an
   external current-user-only staging directory and emits one unsigned archive
   with embedded source provenance.
2. Transfer only that unsigned archive to the Mac and run
   `scripts/release/sign-windows-release.sh --unsigned-archive PATH
   --confirm-sign TAG`, where `TAG`
   is the same strict `vX.Y.Z` tag. It signs with the Certum cloud key
   through `osslsigncode` and the ssign PKCS#11 module, using
   `CERTUM_EMAIL`, `CERTUM_OTP`, `SSIGN_PKCS11_MODULE`,
   `SSIGN_PKCS11_MODULE_SHA256` and `CERTUM_CHAIN_PEM` from the environment
   (`OSSL_PKCS11_ENGINE` names the libp11 engine where it is not found by
   itself). It Authenticode-signs the three authored PE files with a Certum
   timestamp and emits exactly the release ZIP and its checksum in a mode-0700
   external directory.
3. Transfer only those two signed assets to Windows and run
   `scripts/release/verify-signed-windows-release.ps1` with
   `-UnsignedMsiOutputPath <external dir>\labtether-agent-win-x64-unsigned.msi`.
   It re-extracts the ZIP, checks source provenance and checksums, validates
   timestamped Authenticode signatures, runs the signed child and WinUI
   probes, and then builds the per-user MSI from exactly that verified signed
   payload.
4. Transfer only that unsigned MSI to the Mac and run
   `scripts/release/sign-windows-release.sh --signed-assets DIR
   --unsigned-msi PATH --confirm-sign TAG`. It re-verifies the signed ZIP,
   signs the MSI with the same certificate, checks both use one signer, and
   emits the four release assets: ZIP, MSI and their checksums.
5. Transfer those four assets to Windows and run
   `verify-signed-windows-release.ps1` again without `-UnsignedMsiOutputPath`.
   It repeats every ZIP check and also verifies the MSI checksum, its
   timestamped signature from the payload signer, and that an administrative
   image of the MSI holds exactly the verified payload. Keep its proof file
   local; it is not a release asset.
6. Return the four assets and that proof to the Mac, then run
   `scripts/release/publish-windows-release.sh --confirm-draft TAG`. It repeats
   the Mac verification, requires the matching Windows proof for both the ZIP
   and the MSI, refuses to overwrite an existing release, and uploads exactly
   the four assets to a draft. It verifies every GitHub asset name, size,
   state, and SHA-256 digest, then exits while the release is still a draft.
7. Inspect that draft independently. In a new invocation, run
   `scripts/release/publish-windows-release.sh --confirm-publish TAG`. It
   repeats every local check and performs a fresh GitHub readback of the
   existing draft before publishing it. The script rejects both confirmations
   in one invocation, so no command can create and publish a release together.

No private key, password, seed, encoded secret or Windows verification proof
belongs in Git, build artifacts, caches or release uploads. On GitHub the
Certum seed lives only in the `windows-release-signing` environment. Only the
public signer information embedded in the signed PE and MSI files leaves the
signing steps.

## Unsigned release build

When a release ships unsigned, a maintainer can build the ZIP on a hosted
Windows runner instead of steps 1 to 5. Start
`.github/workflows/unsigned-release-build.yml` by hand with `tag`,
`agent_tag` (the same tag) and `confirm` (the tag again). It checks out both
tags, checks that they match the tags on GitHub, and runs
`scripts/release/prepare-unsigned-windows-release.ps1` with all of its release
tests. It then copies the unsigned archive unchanged to
`labtether-agent-win-x64.zip` and writes `labtether-agent-win-x64.zip.sha256`
in the one-line form the Hub's agent manifest reads. It then builds the
per-user installer `labtether-agent-win-x64.msi` from that ZIP's files with
`scripts/build-msi.ps1`, which checks the installer holds exactly those
files, and writes `labtether-agent-win-x64.msi.sha256`. All four files and
`build-record.json` are kept as one run artifact for one day. Publish all
four; the installer is what people download.

The workflow signs nothing and never creates or uploads a GitHub release;
publishing stays a maintainer step.
`scripts/release/test-source-only-workflows.sh` allows this one workflow and
refuses any wider version of it.
