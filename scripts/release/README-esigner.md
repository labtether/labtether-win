# Manual Windows signing with eSigner CKA

This optional signer runs on a locally approved Windows release host. It uses
an existing SSL.com eSigner CKA certificate through the Windows SDK SignTool.
GitHub Actions runs its synthetic tests and parses its PowerShell source; it
never signs, configures CKA or receives signing credentials.

The existing Mac/PFX signing scripts remain available. This path has source
checks and synthetic tests, but still needs an owner-approved real signing
run and final Windows verification before it can be called proven.

## Setup and approval

The owner must already have a suitable certificate enrolled with eSigner and
configure CKA locally using the [SSL.com installation guide](https://www.ssl.com/how-to/how-to-install-ssl-com-esigner-cloud-key-adapter-cka/).
Install the Windows SDK SignTool and Python 3.12 or later. The helper selects
the newest installed x64 SignTool and requires its Microsoft signature.

CKA uses a key held by SSL.com's signing service. This is an explicit choice
of signing provider, not permission to move an existing local private key.
Do not upload PFX files, keys, passwords or one-time-code secrets to GitHub.
Do not put CKA credentials in scripts, arguments, environment files or logs.
The script asks silently for the locally configured certificate thumbprint.

Each real run needs the owner's approval for signing and its charges.
GitHub Actions spending limits do not cover SSL.com. The command requires
the exact release tag again and approval for up to four paid signatures:
three authored payloads and the MSI. It never retries a signing call.
On a shared development host, run under the workspace's resource runner and
lock, with the job's RAM, time, scratch and log limits declared first.

## Prepare and sign the ZIP

Use clean wrapper and Go-agent checkouts at the exact same release tag.
Run `prepare-unsigned-windows-release.ps1` as in the main release guide.
Keep its unsigned ZIP outside both source repositories. Create a separate,
empty output directory outside them, then run:

```powershell
python -B scripts/release/sign_windows_esigner.py `
  --tag vX.Y.Z --agent-repo C:\src\labtether-agent `
  --unsigned-archive C:\release\unsigned\labtether-agent-win-x64-unsigned.zip `
  --output-directory C:\release\signed-zip `
  --confirm-sign vX.Y.Z --confirm-paid-signatures 4
```

Only `LabTetherAgent.exe`, `LabTetherAgent.dll` and
`Assets/labtether-agent.exe` are signed. Other libraries keep their original
bytes. The result is `labtether-agent-win-x64.zip` and its `.sha256` file,
using the same provenance and signed-payload manifest as the Mac/PFX path.

Run `verify-signed-windows-release.ps1` with `-UnsignedMsiOutputPath` to check
the signed ZIP on Windows and build the MSI from those exact payloads.
Keep the verification proof separate from the release assets.

## Sign the MSI and verify

Create a new, empty output directory. Use the same tag, source checkouts,
Windows user and certificate as the ZIP stage:

```powershell
python -B scripts/release/sign_windows_esigner.py `
  --tag vX.Y.Z --agent-repo C:\src\labtether-agent `
  --signed-assets C:\release\signed-zip `
  --unsigned-msi C:\release\msi\labtether-agent-win-x64-unsigned.msi `
  --output-directory C:\release\signed-final `
  --confirm-sign vX.Y.Z --confirm-paid-signatures 4
```

The signer checks that the MSI holds the exact signed ZIP payload before
making the fourth paid call. The result contains exactly four assets: ZIP,
MSI and their checksums. Each signature must be valid, timestamped and from
the selected certificate. Run `verify-signed-windows-release.ps1` again on
all four files, without `-UnsignedMsiOutputPath`.

This script never publishes. The existing Mac publisher still requires the
four verified assets and Windows proof, followed by separate draft and
publish approvals.

## Saved state and failures

The four-signature budget and reusable signed files stay beneath
`%LOCALAPPDATA%\LabTether\ReleaseSigning\<tag>-<wrapper-commit>-<agent-commit>`.
Keep this state. It is separate from scratch and release assets; do not upload
it, delete it to retry, or switch Windows users or hosts to repeat signing.
Changed source creates a new candidate and needs new paid-signing approval.

Before each signer call, a reservation is written and flushed to disk.
A successful signature is saved with its hash. A repeated invocation reuses
those bytes only after checking their hash, certificate and timestamp. A
changed unsigned ZIP, input file or certificate is refused for that candidate.

A failed, interrupted or timed-out signing attempt may already have incurred
a charge. Its reservation stays used, and further calls for that file stop.
There is no reset or force option. Preserve the state and inspect the provider's
result with the owner. A stale operation lock also requires manual inspection;
the tool never removes a lock left by another attempt.
