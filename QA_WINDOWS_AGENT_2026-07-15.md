# Windows Agent installed-product QA — 2026-07-15

## Acceptance state

**NO-GO for installed desktop release acceptance; headless/user-process gates
are green.** Source, packaging, child-runtime, strict-TLS enrollment, durable
credential, local authorization, live Hub telemetry, and two server-restart
reconnect gates are green. The exact desktop candidate is the r22 native
wrapper below, bundling the already proven r19-win Go child.

Interactive Windows desktop acceptance is still required. There is no active
Windows user/explorer session, and the saved RDP connection presents an
unverified remote-computer certificate. That prompt has not been accepted
without the user's explicit approval. The exact r22 WinUI runtime, tray/flyout,
Settings, About, diagnostics Save As, and Start at Login therefore remain
explicitly unproven on the installed candidate rather than being inferred from
tests.

The existing `LabTetherAgent` Windows service was deliberately preserved. It
remains running from `C:\Program Files\LabTether-QA\labtether-agent.exe`; no
service configuration, credential, or retained QA state was replaced.

## Exact final r19-win candidate

- Windows host: `WinDevelopment`, Windows 11 Pro build 26200, user `micha`
- QA payload:
  `C:\Users\micha\LabTetherWinAgentQA\candidate-r19-custom-asset\win-x64`
- Payload: 536 files, 234,643,155 bytes, zero sync-conflict copies
- Native app host: 278,528 bytes, SHA-256
  `A3835AB52F612481A728A204DEA6E2A05A4EC0FD87DB98C2090604FF35C8A41F`
- Managed wrapper DLL: 427,520 bytes, SHA-256
  `79F21951A01915E50EB15977C10EEDE1698B40F88B083DAC97C99CA722175DD3`
- Bundled Go child: 14,359,552 bytes, SHA-256
  `B2FAFE618324466E6239D5F906E0296FEAF8305C84D7E38A503EEE60653C710E`
- Child version: `qa-20260715-r19-win`
- Required unpackaged resources verified present: application PRI, Windows App
  SDK runtime DLLs, WinUI resource PRIs, child binary, and `AGENT_VERSION`
- This local QA app host is intentionally unsigned. Release acceptance still
  requires the release workflow's Authenticode-signed payload.

The child was rebuilt from the current `labtether-agent` working tree rather
than reusing the installed r8 binary. Source HEAD at build time was
`2793668a47562f2b1f53952bf4b3a5f36f3be7eb`; the working tree intentionally
contained the current uncommitted hardening work. The immediately preceding
live r18 child was 14,358,528 bytes with SHA-256
`F218E8E272E55D646F2A5956181E1DE36C4E1407AA997BA064CFF867F568A9EF`.
The previously installed r8 binary is 14,347,776 bytes with SHA-256
`C712916FBF868BBA8B4370B993201C4137C8D1F85E235D9336E14C22773DE56D`.

## Exact r22 native-wrapper candidate — 2026-07-16

The r19-win child remains unchanged and online while a bounded native-wrapper
repair is awaiting an interactive desktop. The replacement candidate is:

- QA payload:
  `C:\Users\micha\LabTetherWinAgentQA\candidate-r22-native-group-intent\win-x64`
- Payload: 536 files, 234,644,799 bytes, zero sync-conflict copies
- Native app host: 278,528 bytes, SHA-256
  `A3835AB52F612481A728A204DEA6E2A05A4EC0FD87DB98C2090604FF35C8A41F`
- Managed wrapper DLL: 429,056 bytes, SHA-256
  `F47C6ACA8E0A925084AD9A0E3135D74FEA14D3D866C0772D4D7676CB41997CAD`
- Bundled child: exact r19-win bytes, hash, and version documented above
- Native Windows Release VSTest: **204/204 passed**
- NuGet vulnerability audit including transitives: no known vulnerabilities
- Publish validation: clean deterministic output, required WinUI resources,
  identity-free application PRI, and child hash/version parity passed
- Source parity for every file in this bounded repair was verified between the
  local working tree and the isolated native Windows build source

The app host remains intentionally unsigned because `WinDevelopment` has no
code-signing certificate with a private key and no usable signing tool on its
PATH. This candidate is suitable for local QA only; a public release still
requires an Authenticode-signed artifact from the release workflow.

## User-visible defects found and repaired

1. **False connection and empty telemetry in the tray UI.** The wrapper treated
   any reachable local API response as hub-connected and deserialized obsolete
   flat fields. The current Go agent returns `connected`, `connection_state`,
   nested `metrics`, `agent_version`, `device_fingerprint`, and current alert
   title/summary fields. The wrapper now maps the real contract, retains legacy
   compatibility, and keeps a locally reachable but hub-disconnected child
   visibly disconnected.
2. **About showed no child version/fingerprint.** It queried `/agent/info`, which
   is only the endpoint-helper health response. It now reads those fields from
   `/agent/status`.
3. **Onboarding API-token choice did not change authentication mode.** The API
   radio button was not bound to the view model. Both token choices now round
   trip correctly.
4. **Diagnostics Save As failed and lacked reliable feedback.** About and Logs
   exports now initialize the picker with the owning HWND, use a Windows-safe
   basename, write to the selected path, handle cancel, prevent duplicate
   clicks, and show success/error feedback.
5. **Diagnostics could reproduce secrets from raw logs.** Export now redacts
   configured API, enrollment, TURN, and local-API secrets plus common
   authorization/token/password/secret patterns, including quoted JSON-style
   fields and URL query values, from every text entry.
6. **Invalid settings could mutate Start at Login before validation failed.**
   URL and log-level validation now occurs before login registration or settings
   persistence.
7. **Logs “Export” silently wrote to a temporary path and preserved secrets.**
   It now uses a real Save As flow, applies the diagnostics redactor, and
   reports the selected destination or error.
8. **The tray could stay green after the child stopped or crashed.** Stopping
   the local poll loop now invalidates the last hub state immediately, so crash
   backoff and user-initiated stop are shown as disconnected.
9. **Logs Auto-scroll was cosmetic and closed windows leaked subscriptions.**
   New matching lines now scroll the real list when enabled, child output is
   marshalled to the UI context, and closing the window unsubscribes it.
10. **The metrics Pop Out was not actually always on top.** Its WinUI
    `OverlappedPresenter` now enables the promised always-on-top behavior.
11. **Actionable child states were flattened to “Disconnected.”** Flyout and
    pop-out now distinguish Connecting and Authentication Failed while keeping
    unknown internal states safely generic.
12. **Custom CA trust existed in the child but had no user path.** Onboarding,
    its hub probe, and Settings now accept a regular PEM CA file and validate a
    private chain while still enforcing hostname matching. TLS skip remains an
    explicit temporary-development choice; the expanded page is scroll-safe at
    the shipped window size.
13. **Connection and alert toast methods were never wired.** The app now
    notifies only on real post-startup disconnect/reconnect transitions and
    throttles firing-alert notices through the existing notification service.
14. **Closing first-run setup stranded the user.** The tray now exposes a
    single-instance Setup / Re-enroll action, prefills only non-secret state,
    and restarts an existing child after successful re-enrollment.
15. **Launching the app a second time redirected activation but did nothing.**
    The existing instance now brings its flyout forward on redirected launch.
16. **Custom CA and “skip verification” could be enabled together.** The
    insecure option silently won even though a user could reasonably believe
    their chosen CA was enforced. Onboarding and Settings now reject the
    contradiction before network or login-state mutation; legacy persisted
    conflicts securely prefer the custom CA.
17. **Settings accepted arbitrary readable text as a custom CA.** It then
    persisted the path and restarted a previously working agent before the PEM
    failure surfaced. Settings now requires a valid PEM bundle containing a
    self-signed root before changing Start at Login or saving anything.
18. **Intentional Quit could emit a false disconnect/retrying toast.** Shutdown
    stopped local polling while connection notifications were still subscribed.
    It now detaches and unregisters notification handlers before stopping the
    child, so an intentional exit is quiet.
19. **A protected-but-world-readable bearer file counted as durable private
    state.** The old check only required a nonempty regular file with inherited
    ACLs disabled. It now verifies the owner and rejects every allow rule except
    the current user, SYSTEM, and local Administrators, while requiring current-
    user read access.
20. **The child ignored the wrapper-parent lifetime contract.** A real Windows
    probe killed the configured dummy parent and the r16 child remained alive
    beyond 10 seconds because `LABTETHER_PARENT_PID` was exported but never
    consumed. The Go child now monitors the exact Windows process handle and
    cancels itself when that parent exits (with a Unix liveness fallback), while
    invalid/unavailable configured parents fail closed instead of starting an
    orphan.
21. **A user-entered Asset ID was ignored during first enrollment.** The native
    wrapper correctly exported `AGENT_ASSET_ID`, but the Go enrollment request
    always preferred `os.Hostname()` when it was available. Live QA requested
    `windows-r18-user-qa` and the Hub instead received `WinDevelopment`. First
    enrollment now prefers a validated configured Asset ID, falls back to a
    valid system hostname, then to a safe fixed identity. Focused regression
    coverage proves the configured, invalid-override, hostname, and safe-default
    branches.
22. **A one-time onboarding group was replayed after enrollment.** The wrapper
    persisted free-form `GroupId` and exported it on every child launch. The
    Hub correctly normalized a nonexistent `qa` group to unplaced at enrollment,
    but later REST heartbeats replayed `qa` and failed raw group validation with
    HTTP 400 before authenticated placement preservation ran. The r22 wrapper
    now exports group only with a fresh enrollment token, waits for the protected
    durable bearer, removes the consumed enrollment credential and group intent,
    then performs one controlled restart without trying to override the Hub's
    canonical placement. Legacy durable installations are migrated on launch,
    while a fresh re-enrollment token explicitly preserves its new group intent.
    The wizard now labels and enables the field only for enrollment tokens, and
    clears it when API-token mode is selected instead of silently ignoring it.
23. **Setup declared success after checking only that the Hub was reachable.**
    A syntactically valid but rejected one-use token passed the `/version`
    preflight, so the wizard displayed Connecting/success, closed, and left the
    child retrying without telling the user their credential was rejected. The
    Go child now exposes a sanitized `enrollment_token_rejected` terminal state
    through its authenticated localhost status contract. The native wizard
    starts the exact child itself, remains open, and completes only after real
    authenticated connectivity plus durable-token persistence and one-use-token
    cleanup. Rejections and persistence failures stay visible with actionable
    copy; retrying invalidates late status from the replaced child, and closing
    during preflight cannot persist settings or start a child in the background.

## Evidence completed

- `git diff --check`: pass in both Windows wrapper and Go child checkouts.
- Current Go child: `go test ./...`, `go test -race ./...`, and `go vet ./...`
  passed every package. Windows amd64 and arm64 release-equivalent cross-builds
  passed; the amd64 output is the exact bundled r19-win child above.
- The setup-success repair passed repository-wide Go tests/vet, focused Go race
  coverage, 5 focused onboarding state-machine tests, and 31 focused native
  local-status/classification tests. The C# subsets were compiled and run in a
  platform-independent harness because WinUI's full XAML compiler requires
  Windows; a fresh Windows Release VSTest, publish, and user-path replay remain
  required before accepting the next candidate.
- Fresh, conflict-free exact-r19-win wrapper source: direct Windows VSTest
  **199/199 passed**.
- Fresh, conflict-free exact-r22 wrapper source: direct Windows Release VSTest
  **204/204 passed**; the transitive vulnerability audit remained clean.
- NuGet vulnerability audit including transitives: no known vulnerable
  packages from the configured source.
- Child version smoke on Windows: both `help` and `--console help` reported
  `labtether-agent qa-20260715-r18`.
- Isolated exact-r18 runtime on Windows port 18118: local authenticated status
  became ready, reported the requested asset ID and exact r18 version, exposed
  a device fingerprint, and populated CPU 66.28%, memory 19%, and disk 76.07%.
  It correctly remained `connecting`/not connected against an intentionally
  unavailable endpoint.
- Exact-r18 localhost authorization: no credential and an incorrect credential
  each returned 401; the correct per-process credential returned 200; the
  listener bound only to `127.0.0.1`.
- Exact-r18 parent-death proof on Windows port 18119: after status became ready,
  the configured dummy parent was killed. The child logged the containment
  event, exited autonomously within 15 seconds, and disappeared from the process
  table. Both isolated runs released their ports and removed all temporary
  state.
- Existing installed service after all isolated work: Running and still exact
  r8 bytes/hash above.
- Windows default trust correctly rejected the private Hub CA. The exact child
  then enrolled with strict hostname/chain verification using the explicit CA
  file (SHA-256
  `C66CDA051E36371F76967F6B7599D3261467F4CB49C69DAC08BCEA8DCDA800FB`);
  TLS verification was not disabled.
- A first live enrollment with the deliberately nonexistent group `qa` exposed
  a Hub foreign-key 500. The token remained unconsumed. Retrying the same token
  with the wrapper-equivalent empty group enrolled r18 as canonical asset
  `windevelopment`, removed the one-time file, and wrote a 44-byte durable
  credential. Its DACL had inheritance disabled and only current user, SYSTEM,
  and local Administrators FullControl rules. Neither one-time nor durable
  secret appeared in any child log.
- The final r21/r19-win enrollment repeated that same user path with the fixed
  contracts. Exact custom `AGENT_ASSET_ID=windows-r20-user-qa` won over the OS
  hostname, deliberately stale `AGENT_GROUP_ID=qa` was normalized to SQL NULL,
  and Hub API showed the exact r19-win version plus current CPU, memory, disk,
  RX, and TX telemetry. The one-time credential was removed and the durable
  credential had the same protected three-principal DACL. Enrollment and
  durable secrets were absent from logs.
- Live connected local authorization remained loopback-only: missing and wrong
  credentials returned 401, the correct ephemeral credential returned 200, and
  the listener bound only to `127.0.0.1`. The device-fingerprint file SHA-256
  was `C437CB6C517C5D286EEC8836D25BBFBB96A11EA0CEE263607971B1272E5D70B8`.
- A controlled child stop and restart loaded the durable credential and returned
  to authenticated `connected` state without an enrollment token.
- Hub r18 independently showed the Windows asset online with exact agent
  version and 12 points each for CPU, memory, disk, and network telemetry.
- The same held r18 child survived two in-place Hub upgrades without process
  intervention. Across r20 it saw refused connections at 14:40:47Z and
  14:40:50Z and reconnected at 14:40:52Z. Across r21 it received the Hub's
  shutdown signal at 14:47:31Z and reconnected at 14:47:32Z. Child PID 336 and
  holder PID 5408 were unchanged through both proofs; post-r21 metrics remained
  populated.
- Stopping holder PID 5408 exercised the parent-lifetime contract again: child
  PID 336 exited, port 18120 was released, and ephemeral local-auth/PID files
  were removed. The retained r8 service remained Running/Automatic with exact
  original bytes/hash.
- The final r19-win child also passed the deliberate Hub restart gate without
  process intervention. It received Hub shutdown at 14:58:25Z, stayed locally
  reachable in `connecting`, and first reconnected at 14:59:08Z. The Hub then
  became unstable and unavailable: the socket flapped from 15:03:28Z and ten
  independent HTTPS probes failed TCP connection from 15:06:19Z through
  15:06:34Z. The child correctly kept retrying while the Hub failed closed and
  auto-restarted, recovered at 15:07:54Z, then passed a separate 300-second
  uninterrupted connected window from 15:09:15Z through 15:14:15Z. Parent PID
  7216 and child PID 4068 were unchanged throughout; final status retained the
  exact version/custom asset and populated CPU 17.79%, memory 19%, and disk
  76.77%.
- R21 was superseded after that evidence exposed the Hub runtime-lease loss.
  The same Windows PIDs were carried unchanged into corrected r22. During the
  r22 replacement, local state moved connected -> disconnected at
  15:24:57.750Z -> connecting at 15:24:58.399Z -> connected at 15:24:59.248Z,
  then stayed continuously connected for 300 seconds through 15:30:07.066Z.
- A separate deliberate r22 restart then proved the final recovery path without
  a version change: disconnected at 15:31:50.714Z, reconnected at
  15:31:51.620Z, and stayed continuously connected from 15:32:01.895Z through
  15:37:02.387Z. Parent PID 7216 and child PID 4068 remained unchanged across
  both r22 transitions. Final local status at 15:37:15.659Z retained exact
  r19-win version/custom asset/fingerprint with CPU 24.98%, memory 20%, disk
  76.73%; strict-CA Hub health returned HTTP 200 with PostgreSQL OK.
- Matching final Hub evidence showed the asset online, `group_id` NULL, source
  `agent`, exact r19-win version, reconnect count 7, and a connected Windows
  registry entry. The 15-minute series contained 45 current points each for
  CPU, memory, disk, RX, and TX through 15:39:45Z. The post-restart audit saw
  661 requests, zero HTTP errors, and no lease, drain, closed-pool, or fatal
  signature.
- The obsolete `windevelopment` QA asset and credential were removed/revoked
  through the Hub, and its local r18 state was deleted. The final r19 state is
  intentionally still held online pending campaign-level cleanup direction.
- Overnight connection churn did not restart or crash the Windows child. Parent
  PID 7216 and child PID 4068 remained unchanged. The first receive timeout at
  08:25:05 Sydney time followed the Hub Mac's 08:24:32 clamshell sleep; buffered
  telemetry replayed after the 08:40 wake. Later timeouts and reconnects line up
  with the Mac's maintenance sleep/dark-wake cycles, including the Hub shutdown
  at 08:48:15 and reconnect at 08:48:33. The child buffered and replayed samples
  as designed. This evidence assigns the churn to Hub-host availability, not a
  Windows agent stability defect.
- At the final read-only check, r19-win remained connected with live CPU,
  memory, and disk telemetry. Its holder was still PID 7216 and its child PID
  4068, both in Session 0; the preserved r8 service was still PID 3844. There
  was no `explorer.exe` and `quser` reported no user, so a usable interactive
  desktop still did not exist.
- The sole exact-candidate Application Error was the deliberate Session-0
  native wrapper attempt at 2026-07-15 14:47:12Z in `Microsoft.UI.Xaml.dll`,
  before the held child started at 14:56:46Z. There was no exact-candidate
  application failure after that held child start. This is evidence that WinUI
  cannot be certified from OpenSSH Session 0, not a Go child crash.
- The exact r22 native candidate likewise has one expected Session-0 build-probe
  baseline event at 2026-07-16 09:01:43Z in `Microsoft.UI.Xaml.dll`. Desktop QA
  must baseline that existing event and require the count not to increase after
  the active-session runtime probe and user workflows.
- Native desktop pre-state was captured: `%LOCALAPPDATA%\LabTether` contained no
  files or settings, no native wrapper process was running, and the HKCU Run
  value `LabTetherAgent` was absent. Desktop QA must restore that absent Run
  value after exercising Start at Login.

The publish script validated clean output, required resource files, the
identity-free `Application` PRI map, and child hash/version parity before its
final GUI probe. Child help was verified separately on Windows. The WinUI
runtime probe cannot complete in OpenSSH session 0, where there is no
interactive desktop. That probe remains an interactive acceptance gate; it is
not reported as passed.

The package audit reports xUnit 2.9.3 and old test-only transitives as
deprecated/legacy, with xUnit v3 as the suggested migration. No vulnerable
package was reported. Publish also emits CommunityToolkit source-generator AOT
advisories for field-based observable properties; this payload is not a
NativeAOT publish, so those are tracked release-hygiene warnings rather than a
demonstrated runtime failure.

## Remaining interactive acceptance gates

After explicit approval of the saved remote-desktop certificate prompt and
availability of an active Windows desktop session:

1. Record the active user/session and a fresh Application/WER baseline. Recheck
   the exact r22 app-host/DLL/child hashes above. Do not stop the retained r19
   holder or the r8 service.
2. Run the exact r22 `LabTetherAgent.exe --winui-runtime-probe` from the active
   desktop and require exit 0 with no new WER/AppCrash record.
3. Create a disposable one-use Hub enrollment token. Launch exact r22 and use
   the real onboarding wizard with strict TLS, custom CA
   `C:\Users\micha\LabTetherWinAgentQA\r19-material\r21-ca.crt`, a unique
   disposable Asset ID, and deliberately stale group `qa`.
4. Prove the full enrollment lifecycle: connected UI, protected durable token,
   consumed one-use token removed, persisted `GroupId` cleared, exactly one
   controlled child restart, Hub placement left canonical/unplaced, and no
   periodic heartbeat HTTP 400 after restart.
5. Verify tray icon and flyout, real Hub-connected state, nonzero
   CPU/memory/disk, alerts, always-on-top pop-out, log viewer filter/auto-scroll
   and Save As, Settings validation/save, About version/fingerprint, diagnostics
   Save As and ZIP redaction, reconnect UX, and update-check failure behavior.
6. Exercise Start at Login on and off through the UI, then require the original
   HKCU Run state: `LabTetherAgent` absent.
7. Quit from the actual tray UI. Require wrapper and its child to terminate,
   the ephemeral local-API credential to be removed, no false disconnect toast,
   no orphan, and no new WER/AppCrash record.
8. Recheck that the r8 `LabTetherAgent` service, MomentBackup runner, Beszel,
   and held r19 process/state were unchanged. Only after final Hub evidence is
   captured, remove the disposable desktop-QA asset and artifacts created by
   this sequence.

## Exact r27 desktop re-certification — 2026-07-17

This section supersedes the earlier statement that no interactive Windows
desktop was available. A live RDP Session 2 was available and the exact r27
candidate was launched from
`C:\Users\micha\LabTetherWinAgentQA\candidate-r27\win-x64` with no working
directory supplied, exercising the repaired startup-path normalization.

### Exact artifact and source evidence

- Source archive: 120 files, 14,825,283 uncompressed bytes, SHA-256
  `A60719F3DE9ACD3C53D7104FEDCA394B089FF0C43392BABF33F86A845AA1C3E1`.
- Published candidate: 536 files, 234,677,083 bytes.
- Wrapper EXE SHA-256:
  `60C090812DC05C3272A9E2175E52D0CFFF5AF1612BD2950DBCD524462FDD8C84`.
- Wrapper DLL SHA-256:
  `FFBF7769BCECB4B5FB64B45ED2ABC27D01399647C93DB6623AFA7067E05B03EF`.
- Bundled child SHA-256:
  `4005F1F27C8DE15095557531D1EE6459D793AD81E71D616249B58B1F8E2E6F77`;
  both `--version` and `-v` returned `qa-20260717-r27` with exit code zero.
- EXE/DLL product and file version are `1.4.0` / `1.4.0.0`; managed assembly
  version is `1.4.0.0`. The manifest is `1.4.0.0`, matching the latest public
  release version and removing the prior false-update condition.
- Windows Release VSTest: **221/221 passed**. The unpackaged publish validator,
  full compiled-XAML runtime probe, archive/PRI integrity checks, tag-version
  validation, workflow YAML/actionlint, XML validation, and `git diff --check`
  passed. Only the known non-NativeAOT CommunityToolkit advisory remains.
- The Application Error and Windows Error Reporting logs contained zero exact
  LabTether wrapper/child crash entries from the r27 launch through the final
  retained retry checkpoint.
- Authenticode status is **NotSigned** for the EXE, DLL, and child. No public
  signing identity was available; therefore a public Windows release remains
  **NO-GO** even though this local candidate is functional.

### Hands-on first-run and failure UX

The prior `%LOCALAPPDATA%\LabTether` state and PasswordVault credential were
backed up under isolated QA names, then live state was cleared. The preserved
r8 service (PID 3844), held r19 child (PID 4068), and holder (PID 7216) were not
stopped or altered.

The visible r27 wizard accepted strict Hub URL
`https://192.168.0.118:28443` and custom CA
`C:\Users\micha\LabTetherWinAgentQA\r19-material\r21-ca.crt`, with TLS skip
disabled. A deliberately invalid enrollment token was submitted for asset
`windows-r27-desktop-qa-20260717` and blank initial group. The wizard remained
open and showed the specific actionable message that the enrollment token was
rejected.

Rollback readback immediately after failure proved:

- the state root contained zero files and no `.setup-*` directory;
- no r27 child process started;
- the live credential remained absent while the isolated backup credential
  remained present (`live=0;backup=1`); and
- the pre-existing r8 and r19 processes were unchanged.

Pressing **Back** returned to the authentication step, cleared the secret field,
and removed the old failure banner. Re-entering and advancing again showed no
stale error. This is the installed proof for the repaired
`OnboardingViewModel` navigation/edit invalidation behavior; the fresh failure
is still retained when a retry itself fails.

### Current exact-r27 boundary

The real one-time Hub token remains valid and unconsumed (`max_uses=1`,
`use_count=0`) because direct RDP synthetic typing does not preserve uppercase
and underscore characters. A secure in-memory pipe and a volatile Mac clipboard
paste were both rejected by the execution safety gate until the owner explicitly
approves that one-time credential transfer. No token value was written to a
file, command, task definition, screenshot, or report.

Consequently, exact-r27 durable enrollment, connected flyout/telemetry,
reconnect, Logs/Settings/About/diagnostics, Start-at-Login restoration, pop-out,
tray Quit/orphan checks, and final disposable-asset cleanup remain pending that
single approval. They are not reported as passed. The active r27 setup window
is intentionally left at a recoverable retry point with zero committed state;
the original native state and credential backup are retained until the live
enrollment either succeeds or is explicitly abandoned.

## r27 24-hour checkpoint — 2026-07-21

Checkpoint result: **not passed / blocked by the installed Hub runtime being
down**.

The checkpoint was run at `2026-07-21T15:53:18Z`. The local installed Hub
runtime was unavailable before any current Windows asset check could be made:
the selected Docker context was `colima`, `docker ps` failed because
`/Users/michael/.colima/default/docker.sock` was absent, the default Docker
context also had no `/var/run/docker.sock`, and `colima status` reported
`colima is not running`. `https://192.168.0.118:28443/version` and `/healthz`
both timed out, and `http://localhost:3000` refused connection.

The Colima host-agent log shows the VM entered `VirtualMachineStateError` at
`2026-07-16T21:49:29+10:00` and stopped Docker socket forwarding at
`2026-07-16T21:49:37+10:00`. Colima was not restarted because the requested
checkpoint was a continuous soak assertion, not a restart/recovery proof.

No one-time Hub enrollment token was transferred, exposed, or consumed during
this checkpoint. The prior exact-r27 Windows boundary remains unchanged:
durable enrollment, connected tray/flyout telemetry, Logs/Settings/About,
diagnostics export, Start-at-Login restoration, reconnect UX, pop-out, tray
Quit/orphan checks, and disposable-asset cleanup are still not reported as
passed. The exact r27 Windows payload remains Authenticode `NotSigned`, so a
public Windows release remains **NO-GO** independently of this blocked Hub
checkpoint.

## r27 24-hour follow-up checkpoint - 2026-07-22

Checkpoint result: **not passed / blocked by the installed Hub runtime being
down**.

This follow-up checkpoint was run at `2026-07-22T02:21:48Z`. The current Docker
context was `default`, but `/var/run/docker.sock` was absent and `docker ps`
could not connect. The Colima Docker socket
`/Users/michael/.colima/default/docker.sock` was also absent, `colima status`
reported `colima is not running`, and no `Docker`, `colima`, `lima`, `qemu`, or
`vz` process was visible to `pgrep` at the checkpoint. Strict probes to
`https://192.168.0.118:28443/version` and `/healthz` timed out with HTTP `000`,
and `http://localhost:3000` refused connection.

The Colima logs show the VM had been started and stopped earlier on
`2026-07-22` (`10:31:18+09:00` to `10:41:27+09:00`, then `10:42:39+09:00` to
`10:44:58+09:00`), with LabTether port forwarding including `3000` and `28443`
torn down before this checkpoint. Colima was not started for this checkpoint
because the requested check is a continuous soak assertion, not a
restart/recovery proof.

The installed Hub was unavailable before any current Windows asset check could
be made. No one-time Hub enrollment token was transferred, exposed, or consumed.
The prior exact-r27 Windows boundary remains unchanged: durable enrollment,
connected tray/flyout telemetry, Logs/Settings/About, diagnostics export,
Start-at-Login restoration, reconnect UX, pop-out, tray Quit/orphan checks, and
disposable-asset cleanup are still not reported as passed. The exact r27
Windows payload remains Authenticode `NotSigned`, so a public Windows release
remains **NO-GO** independently of this blocked Hub checkpoint.

## r27 automation current-system checkpoint - 2026-07-23

Checkpoint result: **not passed for the exact-r27 Windows/Hub lane; the Hub is
running a different candidate and no current Windows presence row exists**.

This read-only automation checkpoint ran at `2026-07-23T02:21:28Z`. Docker was
reachable through context `colima`, but the live `labtetherqar17` application
containers no longer used r27 image
`sha256:7f088d78bfbb26b6a350096110fce2105ba6cd022192f15cc3d6534e5d0e0940`.
Hub, web console, and console ingress instead used
`labtether-installed-qa:20260723-r31-dockerfix-83695f01` /
`sha256:384b9f3f21afb8072b1091d634380d5cbafac07b2e5e4e770b7f768b38608ee1`,
with fresh `2026-07-23T02:13Z`-`02:15Z` start times. Strict private-CA
`/version` returned `20260723-r31-dockerfix-83695f01`, not `20260717-r27`;
`/healthz` reported PostgreSQL `ok`.

PostgreSQL retained container
`d4fd5c5478ef59cf99b7d0923e3c6da2b358dbdaf63a6d49fba8af74ebb038f3`, start
time `2026-07-22T09:25:33.948207088Z`, restart count zero, healthy state,
`OOMKilled=false`, and the preserved QA database. The three current
application containers were also healthy, restart-zero, and `OOMKilled=false`,
with no requested fatal, panic, lease-loss, incomplete-drain, closed-pool, or
shutdown-error strings in their current logs. Because these are r31-dockerfix
containers, they do not prove the r27 continuous-soak window.

The rendered installed console was reachable only at the unauthenticated login
screen during this run because retained auth was stale (`401`), and no
credential or session state was mutated to force access. The active
`agent_presence` table contained only the Mac and r31 Linux QA agents. The
retained Windows asset row still existed as `windows-r20-user-qa` with
`qa-20260715-r19-win`, but its last heartbeat remained
`2026-07-16T21:49:10Z` and there was no current Windows presence row. The
exact-r27 Windows enrollment boundary therefore remains unchanged: durable
enrollment, connected tray/flyout telemetry, Logs/Settings/About, diagnostics
export, Start-at-Login restoration, reconnect UX, pop-out, tray Quit/orphan
checks, and disposable-asset cleanup are still not reported as passed.

No one-time Hub enrollment token was transferred, exposed, or consumed. No
production connector, credential, backup job, recovery unit, prohibited VM, or
household automation was touched. The exact r27 Windows payload remains
Authenticode `NotSigned`, so a public Windows release remains **NO-GO**
independently of this blocked Hub checkpoint.

## r27 automation current-system checkpoint - 2026-07-24

Checkpoint result: **not passed for the exact-r27 Windows/Hub lane; the Hub is
running a different candidate and no current Windows presence row exists**.

This read-only automation checkpoint ran at `2026-07-24T02:27:51Z`. Docker was
reachable through context `colima`, but the live `labtetherqar17` application
containers no longer used r27 image
`sha256:7f088d78bfbb26b6a350096110fce2105ba6cd022192f15cc3d6534e5d0e0940`.
Hub, web console, and console ingress instead used
`labtether-installed-qa:20260723-main-1a4290af` /
`sha256:a4bcfa50ef25967af4759471ebe1ed2ab6ec79b2d711c7e00547092ab4ddd307`.
Strict private-CA `/version` returned `20260723-main-1a4290af`, not
`20260717-r27`; `/healthz` reported PostgreSQL `ok`.

PostgreSQL retained container
`d4fd5c5478ef59cf99b7d0923e3c6da2b358dbdaf63a6d49fba8af74ebb038f3`, start
time `2026-07-22T09:25:33.948207088Z`, restart count zero, healthy state,
`OOMKilled=false`, and the preserved QA database. The three current application
containers were also healthy, restart-zero, and `OOMKilled=false`, with no
requested fatal, panic, lease-loss, incomplete-drain, closed-pool, OOM, or
shutdown-error strings in their current logs since `2026-07-16T16:15:00Z`.
Because these are main-candidate containers, they do not prove the r27
continuous-soak window.

The installed console rendered only the unauthenticated login screen; protected
console API calls returned `401`, and no credential or session state was
mutated to force access. The `agent_presence` table contained zero rows. The
retained Windows asset rows showed later non-r27 QA state
`windows-main-019338de-service-qa` / `qa-20260723-main-019338de` last seen
`2026-07-23T14:24:27.687829Z`, `windows-r35-headless` /
`qa-20260723-r37-win-4ab8e8d9` last seen `2026-07-23T03:58:16.709863Z`, and
the older `windows-r20-user-qa` / `qa-20260715-r19-win` last seen
`2026-07-16T21:49:10.022066Z`. None of those rows is current exact-r27
presence proof.

No one-time Hub enrollment token was transferred, exposed, or consumed. No
production connector, credential, backup job, recovery unit, prohibited VM, or
household automation was touched. The exact r27 Windows payload remains
Authenticode `NotSigned`, so a public Windows release remains **NO-GO**
independently of this blocked Hub checkpoint.
