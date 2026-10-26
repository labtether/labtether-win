# Shared helpers for building and checking the per-user MSI. Dot-source this
# file; it defines functions only.
Set-StrictMode -Version Latest

# Release metadata lives only in the ZIP; installer/Package.wxs excludes it.
$script:MsiExcludedPayloadNames = @("release-provenance.json", "signed-payloads.json")

function Resolve-LabTetherMSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installationPath = & $vswhere -latest -products "*" -requires Microsoft.Component.MSBuild -property installationPath
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
            $candidate = Join-Path $installationPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }
    throw "Visual Studio MSBuild was not found. Install Visual Studio 2026 Build Tools."
}

# Relative path (forward slashes, lower case) -> SHA-256 for every payload file.
function Get-MsiPayloadFileMap([string]$Root) {
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $map = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force)) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Payload contains a reparse point: $($file.FullName)"
        }
        $relative = $file.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        if ($script:MsiExcludedPayloadNames -contains $relative) {
            continue
        }
        $map[$relative.ToLowerInvariant()] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $map
}

# Extracts an administrative image (no install, no registry changes) and
# returns the folder that holds the app, i.e. the image of INSTALLFOLDER.
function Expand-MsiAdministrativeImage([string]$MsiPath, [string]$Destination, [string]$LogPath) {
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    $arguments = @("/a", "`"$MsiPath`"", "/qn", "TARGETDIR=`"$Destination`"", "/l*v", "`"$LogPath`"")
    $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "MSI administrative extraction failed with exit code $($process.ExitCode); see $LogPath"
    }
    $roots = @(Get-ChildItem -LiteralPath $Destination -Recurse -File -Filter "LabTetherAgent.exe" |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.DirectoryName "LabTetherAgent.dll") -PathType Leaf })
    if ($roots.Count -ne 1) {
        throw "MSI does not contain exactly one LabTether Agent install folder"
    }
    return $roots[0].DirectoryName
}

# Proves the MSI installs exactly the payload: same relative paths, same bytes.
function Assert-MsiMatchesPayload([string]$MsiPath, [string]$PayloadRoot, [string]$ScratchRoot) {
    $image = Join-Path $ScratchRoot "msi-admin-image"
    $log = Join-Path $ScratchRoot "msi-admin-image.log"
    if (Test-Path -LiteralPath $image) {
        throw "MSI extraction directory already exists: $image"
    }
    try {
        $installRoot = Expand-MsiAdministrativeImage $MsiPath $image $log
        $expected = Get-MsiPayloadFileMap $PayloadRoot
        $actual = Get-MsiPayloadFileMap $installRoot
        if ($expected.Count -eq 0 -or $actual.Count -ne $expected.Count) {
            throw "MSI holds $($actual.Count) files; the payload has $($expected.Count)"
        }
        foreach ($path in $expected.Keys) {
            if (-not $actual.ContainsKey($path) -or $actual[$path] -cne $expected[$path]) {
                throw "MSI content differs from the payload at $path"
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $image) {
            Remove-Item -LiteralPath $image -Recurse -Force
        }
        Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    }
}
