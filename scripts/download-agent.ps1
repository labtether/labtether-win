param(
    [string]$Arch = "amd64",
    [string]$RepoOwner = "labtether",
    [string]$RepoName = "labtether-agent",
    [string]$OutputDir = "src/LabTetherAgent/Assets"
)

$ErrorActionPreference = "Stop"

$version = (Get-Content (Join-Path $PSScriptRoot ".." "AGENT_VERSION")).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "AGENT_VERSION file is empty or missing."
}

$binaryName = "labtether-agent-windows-$Arch.exe"
$url = "https://github.com/$RepoOwner/$RepoName/releases/download/v$version/$binaryName"
$checksumUrl = "$url.sha256"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outPath = Join-Path $OutputDir "labtether-agent.exe"
$checksumPath = "$outPath.sha256"

Write-Host "Downloading $binaryName v$version..."
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

$size = (Get-Item $outPath).Length
Write-Host "Saved: $outPath ($([math]::Round($size / 1MB, 1)) MB)"
