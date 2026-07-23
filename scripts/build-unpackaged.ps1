[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet("x64", "arm64")][string]$Arch,
    [string]$Configuration = "Release",
    [string]$OutputDir = "build"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installationPath = & $vswhere `
            -latest `
            -products "*" `
            -requires Microsoft.Component.MSBuild `
            -property installationPath
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
            $candidate = Join-Path $installationPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    throw "Visual Studio MSBuild was not found. Install Visual Studio 2022 Build Tools with the Windows App SDK workload."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$rid = if ($Arch -eq "arm64") { "win-arm64" } else { "win-x64" }
$projectPath = Join-Path $repoRoot "src\LabTetherAgent\LabTetherAgent.csproj"
$agentBinary = Join-Path $repoRoot "src\LabTetherAgent\Assets\labtether-agent.exe"
$agentVersion = Join-Path $repoRoot "src\LabTetherAgent\Assets\AGENT_VERSION"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Project file not found: $projectPath"
}
if (-not (Test-Path -LiteralPath $agentBinary -PathType Leaf) -or
    -not (Test-Path -LiteralPath $agentVersion -PathType Leaf)) {
    throw "Bundled agent core is missing. Run scripts/build-bundled-agent.sh first."
}

# VS MSBuild sometimes needs the SDK path made explicit outside a Developer
# PowerShell. Resolve it from the repository's global.json instead of pinning a
# machine-specific SDK patch version.
if ([string]::IsNullOrWhiteSpace($env:MSBuildSDKsPath)) {
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -ne $dotnet) {
        Push-Location $repoRoot
        try {
            $sdkVersion = (& $dotnet.Source --version | Select-Object -First 1).Trim()
        }
        finally {
            Pop-Location
        }
        $sdkPath = Join-Path (Split-Path $dotnet.Source -Parent) "sdk\$sdkVersion\Sdks"
        if (Test-Path -LiteralPath $sdkPath -PathType Container) {
            $env:MSBuildSDKsPath = $sdkPath
        }
    }
}

$requestedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
}
else {
    Join-Path (Get-Location) $OutputDir
}
$outputRoot = [IO.Path]::GetFullPath($requestedOutputRoot)
$publishDir = [IO.Path]::GetFullPath((Join-Path $outputRoot $rid))
$publishParent = [IO.Path]::GetFullPath((Split-Path $publishDir -Parent))
if (-not $publishParent.Equals($outputRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $publishDir -Leaf) -ne $rid) {
    throw "Refusing to clean publish path outside the expected output root: $publishDir"
}
if (Test-Path -LiteralPath $publishDir) {
    $publishItem = Get-Item -LiteralPath $publishDir -Force
    if (($publishItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clean a publish path that is a reparse point: $publishDir"
    }
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (Test-Path -LiteralPath $publishDir) {
    throw "Unable to clean stale publish output: $publishDir"
}
[IO.Directory]::CreateDirectory($publishDir) | Out-Null
$cleanPublishSentinel = Join-Path $publishDir ".labtether-clean-publish"
[IO.File]::WriteAllText($cleanPublishSentinel, "clean")
if (-not (Test-Path -LiteralPath $cleanPublishSentinel -PathType Leaf)) {
    throw "Unable to verify the clean publish directory: $publishDir"
}
Remove-Item -LiteralPath $cleanPublishSentinel -Force

$msbuild = Resolve-MSBuild
Write-Host "Building unpackaged release payload for $rid ($Configuration)..."
# The WinUI/MSIX tooling also generates the application PRI used by an
# unpackaged WinUI process. Disabling it produces a publish folder that builds
# successfully but crashes in Microsoft.UI.Xaml at startup.
& $msbuild `
    $projectPath `
    -restore `
    -t:Publish `
    "-p:Configuration=$Configuration" `
    "-p:RuntimeIdentifier=$rid" `
    "-p:Platform=$Arch" `
    -p:SelfContained=true `
    -p:WindowsPackageType=None `
    -p:EnableMsixTooling=true `
    -p:GenerateAppxPackageOnBuild=false `
    -p:RequireBundledAgent=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    "-p:PublishDir=$publishDir\" `
    -nologo `
    -verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

$publishedApp = Join-Path $publishDir "LabTetherAgent.exe"
$publishedChild = Join-Path $publishDir "Assets\labtether-agent.exe"
$publishedVersion = Join-Path $publishDir "AGENT_VERSION"
$publishedApplicationPri = Join-Path $publishDir "LabTetherAgent.pri"
$publishedWinUiRuntime = Join-Path $publishDir "Microsoft.UI.Xaml.dll"
$publishedWinUiControlsRuntime = Join-Path $publishDir "Microsoft.UI.Xaml.Controls.dll"
$publishedWinUiResources = Join-Path $publishDir "Microsoft.UI.pri"
$publishedWinUiControlsResources = Join-Path $publishDir "Microsoft.UI.Xaml.Controls.pri"
foreach ($requiredPath in @(
    $publishedApp,
    $publishedChild,
    $publishedVersion,
    $publishedApplicationPri,
    $publishedWinUiRuntime,
    $publishedWinUiControlsRuntime,
    $publishedWinUiResources,
    $publishedWinUiControlsResources
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Published payload is missing required file: $requiredPath"
    }
}

$makePri = Get-ChildItem `
    -Path (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin") `
    -Filter "makepri.exe" `
    -File `
    -Recurse `
    -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\$Arch\\makepri\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $makePri) {
    throw "makepri.exe was not found for $Arch; cannot validate the unpackaged application PRI."
}

$priDumpPath = Join-Path ([IO.Path]::GetTempPath()) "labtether-pri-$([Guid]::NewGuid().ToString('N')).xml"
try {
    & $makePri.FullName dump /if $publishedApplicationPri /of $priDumpPath /o | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $priDumpPath -PathType Leaf)) {
        throw "Unable to inspect the published application PRI."
    }

    [xml]$priDump = Get-Content -LiteralPath $priDumpPath -Raw
    $primaryMap = $priDump.SelectSingleNode("//ResourceMap[@primary='true']")
    if ($null -eq $primaryMap -or $primaryMap.name -ne "Application") {
        $actualName = if ($null -eq $primaryMap) { "<missing>" } else { $primaryMap.name }
        throw "Published application PRI has primary map '$actualName'; unpackaged WinUI requires 'Application'."
    }
    if ($null -ne $priDump.SelectSingleNode("//ResourceMap[@name='LabTetherAgent']")) {
        throw "Published application PRI contains the package-style 'LabTetherAgent' resource map."
    }
}
finally {
    Remove-Item -LiteralPath $priDumpPath -Force -ErrorAction SilentlyContinue
}

$sourceVersion = (Get-Content -LiteralPath $agentVersion -Raw).Trim()
$expectedVersion = (Get-Content -LiteralPath $publishedVersion -Raw).Trim()
if ($expectedVersion -ne $sourceVersion) {
    throw "Published child version '$expectedVersion' does not match source version '$sourceVersion'."
}
if ((Get-FileHash -LiteralPath $publishedChild -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $agentBinary -Algorithm SHA256).Hash) {
    throw "Published child agent does not match the source payload."
}

$probeProcess = Start-Process `
    -FilePath $publishedApp `
    -ArgumentList "--winui-runtime-probe" `
    -WorkingDirectory $publishDir `
    -PassThru
if (-not $probeProcess.WaitForExit(30000)) {
    Stop-Process -Id $probeProcess.Id -Force -ErrorAction SilentlyContinue
    throw "Published application did not complete its WinUI runtime probe within 30 seconds."
}
$probeProcess.Refresh()
if ($probeProcess.ExitCode -ne 0) {
    throw "Published application failed its WinUI runtime probe with exit code $($probeProcess.ExitCode)."
}

$helpOutput = @(& $publishedChild help)
if ($LASTEXITCODE -ne 0 -or $helpOutput.Count -eq 0 -or
    $helpOutput[0].Trim() -ne "labtether-agent $expectedVersion") {
    throw "Published child agent failed its version smoke test."
}

Write-Host "Build complete: $publishDir"
Write-Host "Output format: unpackaged self-contained folder (the local release lane signs and ZIPs this payload)."
