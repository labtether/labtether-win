# Releasing the Windows app

This is for maintainers. To install the app, see the [README](../README.md).

For a locally configured SSL.com eSigner CKA certificate, use the
[manual Windows signing flow](../scripts/release/README-esigner.md). It prepares
the same assets with four paid signatures at most per exact source candidate.
The PFX flow below remains available. Neither flow runs from CI.

The tag-triggered GitHub workflow verifies clean matching wrapper and agent
tags, tests, publishes, builds and checks the MSI, and validates the payload.
It intentionally has read-only permissions: it does not receive signing
material, sign bytes, keep a release archive or installer, attest an archive,
or upload a release.

Official Windows releases use a split local process:

1. On Windows, run `scripts/release/prepare-unsigned-windows-release.ps1` from
   clean matching tagged wrapper and agent checkouts. It builds into an
   external current-user-only staging directory and emits one unsigned archive
   with embedded source provenance.
2. Transfer only that unsigned archive to the Mac and run
   `scripts/release/sign-windows-release.sh --unsigned-archive PATH
   --confirm-sign TAG`, where `TAG`
   is the same strict `vX.Y.Z` tag. The script reads the local
   certificate path and password silently from `/dev/tty`, consumes the
   certificate in place through a file descriptor, Authenticode-signs the three
   authored PE files with `osslsigncode`, and emits exactly the release ZIP and
   its checksum in a mode-0700 external directory.
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

No signing file, private key, password, encoded secret, local certificate path,
or Windows verification proof belongs in Git, GitHub Actions, build artifacts,
caches, or release uploads. Only the public signer information inherently
embedded in the verified signed PE and MSI files leaves the local signing
lane.
