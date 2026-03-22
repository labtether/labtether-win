param(
    [Parameter(Mandatory)][ValidateSet("x64", "arm64")][string]$Arch,
    [string]$Configuration = "Release",
    [string]$OutputDir = "build"
)

$ErrorActionPreference = "Stop"

$rid = if ($Arch -eq "arm64") { "win-arm64" } else { "win-x64" }
$projectPath = Join-Path $PSScriptRoot ".." "src" "LabTetherAgent" "LabTetherAgent.csproj"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

# Check that the agent binary exists
$agentBinary = Join-Path $PSScriptRoot ".." "src" "LabTetherAgent" "Assets" "labtether-agent.exe"
if (-not (Test-Path $agentBinary)) {
    Write-Host "Agent binary not found. Run download-agent.ps1 first."
    Write-Host "  Expected: $agentBinary"
    throw "Missing agent binary."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "Building MSIX for $rid ($Configuration)..."
dotnet publish $projectPath `
    -c $Configuration `
    -r $rid `
    --self-contained true `
    -p:Platform=$Arch `
    -o (Join-Path $OutputDir $rid)

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Build complete: $(Join-Path $OutputDir $rid)"
