param(
    [string]$Arch = "amd64",
    # Defaults to AGENT_VERSION. Releases bundle the Go agent built from the
    # labtether-agent tag that matches the wrapper tag (see release.yml), so
    # keep AGENT_VERSION equal to the wrapper release being prepared.
    [string]$Version = "",
    [string]$RepoOwner = "labtether",
    [string]$RepoName = "labtether-agent",
    [string]$OutputDir = "src/LabTetherAgent/Assets"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content (Join-Path $PSScriptRoot ".." "AGENT_VERSION")).Trim()
}
$tag = "v" + $Version.Trim().TrimStart("v")
if ($tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Agent version '$Version' is not a vX.Y.Z release tag."
}

$binaryName = "labtether-agent-windows-$Arch.exe"
$url = "https://github.com/$RepoOwner/$RepoName/releases/download/$tag/$binaryName"
$checksumUrl = "$url.sha256"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outPath = Join-Path $OutputDir "labtether-agent.exe"
$checksumPath = "$outPath.sha256"
$versionPath = Join-Path $OutputDir "AGENT_VERSION"

Write-Host "Downloading $binaryName $tag..."
Write-Host "  URL: $url"

try {
    Invoke-WebRequest -Uri $url -OutFile $outPath -UseBasicParsing
    Invoke-WebRequest -Uri $checksumUrl -OutFile $checksumPath -UseBasicParsing
} catch {
    Remove-Item -LiteralPath $outPath, $checksumPath -Force -ErrorAction SilentlyContinue
    throw "Failed to download agent binary: $_"
}

$expectedHash = ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash -LiteralPath $outPath -Algorithm SHA256).Hash
if ([string]::IsNullOrWhiteSpace($expectedHash) -or $actualHash -ne $expectedHash) {
    Remove-Item -LiteralPath $outPath, $checksumPath -Force -ErrorAction SilentlyContinue
    throw "Downloaded agent checksum did not match the published SHA-256."
}
Remove-Item -LiteralPath $checksumPath -Force

# The project and build-unpackaged.ps1 require the version marker beside the
# child, matching what build-bundled-agent.sh writes for source builds.
Set-Content -LiteralPath $versionPath -Value $tag -Encoding ASCII

$size = (Get-Item $outPath).Length
Write-Host "Saved: $outPath ($([math]::Round($size / 1MB, 1)) MB), version $tag"
