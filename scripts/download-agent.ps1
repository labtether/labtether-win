param(
    [string]$Arch = "amd64",
    [string]$RepoOwner = "labtether",
    [string]$RepoName = "labtether",
    [string]$OutputDir = "src/LabTetherAgent/Assets"
)

$ErrorActionPreference = "Stop"

$version = (Get-Content (Join-Path $PSScriptRoot ".." "AGENT_VERSION")).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "AGENT_VERSION file is empty or missing."
}

$binaryName = "labtether-agent-windows-$Arch.exe"
$url = "https://github.com/$RepoOwner/$RepoName/releases/download/v$version/$binaryName"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outPath = Join-Path $OutputDir "labtether-agent.exe"

Write-Host "Downloading $binaryName v$version..."
Write-Host "  URL: $url"

try {
    Invoke-WebRequest -Uri $url -OutFile $outPath -UseBasicParsing
} catch {
    throw "Failed to download agent binary: $_"
}

$size = (Get-Item $outPath).Length
Write-Host "Saved: $outPath ($([math]::Round($size / 1MB, 1)) MB)"
