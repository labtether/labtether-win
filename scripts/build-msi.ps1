[CmdletBinding()]
param(
    # Published app folder (build-unpackaged.ps1 output or a verified signed
    # release payload). Its files are installed exactly as they are.
    [Parameter(Mandatory)][string]$PayloadDir,
    [Parameter(Mandatory)][string]$OutputPath,
    # X.Y.Z. Defaults to the product version of the payload's LabTetherAgent.exe.
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "msi-tools.ps1")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "installer\LabTetherAgent.Installer.wixproj"
$payload = (Resolve-Path -LiteralPath $PayloadDir).Path.TrimEnd('\')
if ($payload.Contains(';')) {
    throw "Payload path must not contain ';': $payload"
}
foreach ($required in @(
    "LabTetherAgent.exe",
    "LabTetherAgent.dll",
    "LabTetherAgent.pri",
    "AGENT_VERSION",
    "Assets\labtether-agent.exe"
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) {
        throw "Payload is missing required file: $required"
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload "LabTetherAgent.exe")).ProductVersion
    $Version = ([string]$productVersion -split '[+-]', 2)[0]
}
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "MSI version '$Version' is not X.Y.Z."
}

$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($output) -ne ".msi") {
    throw "Output path must end in .msi: $output"
}
if (Test-Path -LiteralPath $output) {
    throw "Refusing to overwrite an existing file: $output"
}
[IO.Directory]::CreateDirectory((Split-Path $output -Parent)) | Out-Null

# Keep restore and build output out of the source tree so release checkouts
# stay clean.
$work = Join-Path ([IO.Path]::GetTempPath()) "labtether-msi-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($work) | Out-Null
try {
    $msbuild = Resolve-LabTetherMSBuild
    Write-Host "Building per-user MSI $Version from $payload..."
    & $msbuild `
        $project `
        -restore `
        -t:Build `
        -p:Configuration=Release `
        -p:Platform=x64 `
        "-p:PayloadDir=$payload" `
        "-p:ProductVersion=$Version" `
        "-p:BaseIntermediateOutputPath=$work\obj\" `
        "-p:OutputPath=$work\bin\" `
        -nologo `
        -verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed with exit code $LASTEXITCODE"
    }
    $built = @(Get-ChildItem -LiteralPath (Join-Path $work "bin") -Recurse -File -Filter "*.msi")
    if ($built.Count -ne 1) {
        throw "MSI build produced $($built.Count) packages; expected exactly one."
    }
    Assert-MsiMatchesPayload $built[0].FullName $payload $work
    Move-Item -LiteralPath $built[0].FullName -Destination $output
}
finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}

$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Built per-user MSI: $output"
Write-Host "msi_sha256=$hash"
