[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet("x64", "arm64")][string]$Arch,
    [string]$Configuration = "Release",
    [string]$OutputDir = "build"
)

$ErrorActionPreference = "Stop"

# Compatibility entry point. This script historically claimed to build an
# MSIX, but it has always produced the unpackaged folder shipped in the signed
# release ZIP. Keep existing local invocations working while naming the output
# honestly in the canonical script.
Write-Warning "build-msix.ps1 does not produce an MSIX. Building the unpackaged release payload via build-unpackaged.ps1."

& (Join-Path $PSScriptRoot "build-unpackaged.ps1") `
    -Arch $Arch `
    -Configuration $Configuration `
    -OutputDir $OutputDir
