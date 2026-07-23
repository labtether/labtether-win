[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')][string]$Tag,
    [Parameter(Mandatory)][string]$AgentRepo,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
. (Join-Path $PSScriptRoot "windows-release-policy.ps1")

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$AgentRepo = (Resolve-Path $AgentRepo).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$UnsignedArchiveName = "labtether-agent-win-x64-unsigned.zip"
$AuthoredPayloads = @(
    "LabTetherAgent.exe",
    "LabTetherAgent.dll",
    "Assets\labtether-agent.exe"
)

function Invoke-Git([string]$Repository, [string[]]$Arguments) {
    $Output = @(& git -C $Repository @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed for a release source checkout"
    }
    return ($Output -join "`n").Trim()
}

function Assert-CleanTaggedSource([string]$Repository, [string]$ReleaseTag) {
    $Status = Invoke-Git $Repository @("status", "--porcelain=v1", "--untracked-files=all")
    if (-not [string]::IsNullOrWhiteSpace($Status)) {
        throw "Release source checkout is not clean"
    }
    $Head = Invoke-Git $Repository @("rev-parse", "HEAD")
    $TaggedCommit = Invoke-Git $Repository @("rev-list", "-n", "1", $ReleaseTag)
    if ($Head -ne $TaggedCommit) {
        throw "Release source checkout is not at the requested tag"
    }
    return $Head
}

function Test-PathWithin([string]$Candidate, [string]$Parent) {
    $CandidateFull = [IO.Path]::GetFullPath($Candidate).TrimEnd('\') + '\'
    $ParentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    return $CandidateFull.StartsWith($ParentFull, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-ExternalPath([string]$Path) {
    if ((Test-PathWithin $Path $RepoRoot) -or (Test-PathWithin $Path $AgentRepo)) {
        throw "Release staging must be outside both source repositories"
    }
}

function Protect-PrivateDirectory([string]$Path) {
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    $Item = Get-Item -LiteralPath $Path -Force
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Private release directory must not be a reparse point"
    }
    $Sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls.exe $Path /inheritance:r /grant:r "*$Sid`:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to apply a current-user-only ACL to release staging"
    }
}

function Resolve-MSBuild {
    $Command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $Command) {
        return $Command.Source
    }
    $VsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $VsWhere -PathType Leaf)) {
        throw "Visual Studio MSBuild is unavailable"
    }
    $InstallationPath = & $VsWhere -latest -products "*" -requires Microsoft.Component.MSBuild -property installationPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($InstallationPath)) {
        throw "Visual Studio MSBuild is unavailable"
    }
    $Candidate = Join-Path $InstallationPath "MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) {
        throw "Visual Studio MSBuild is unavailable"
    }
    return $Candidate
}

function Assert-ExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}

function Resolve-WindowsVersions([string]$ReleaseTag) {
    $Version = $ReleaseTag.Substring(1)
    $NumericVersion = ($Version -split '[+-]', 2)[0]
    $Parts = @($NumericVersion -split '\.')
    $BinaryParts = @(0, 0, 0, 0)
    for ($Index = 0; $Index -lt $Parts.Count; $Index++) {
        $Value = [int]$Parts[$Index]
        if ($Value -gt 65534) {
            throw "Release version component exceeds the Windows limit"
        }
        $BinaryParts[$Index] = $Value
    }
    return [ordered]@{
        Product = $Version
        Binary = ($BinaryParts -join ".")
    }
}

$WrapperCommit = Assert-CleanTaggedSource $RepoRoot $Tag
$AgentCommit = Assert-CleanTaggedSource $AgentRepo $Tag
Assert-TrackedSourcePolicy $RepoRoot
Assert-TrackedSourcePolicy $AgentRepo
$Versions = Resolve-WindowsVersions $Tag
Assert-ExternalPath $OutputDirectory

if (Test-Path -LiteralPath $OutputDirectory) {
    $OutputItem = Get-Item -LiteralPath $OutputDirectory -Force
    if (($OutputItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Output directory must not be a reparse point"
    }
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
        throw "Output directory must be empty"
    }
}
Protect-PrivateDirectory $OutputDirectory

$WorkRoot = Join-Path ([IO.Path]::GetTempPath()) "labtether-win-prepare-$([Guid]::NewGuid().ToString('N'))"
Assert-ExternalPath $WorkRoot
Protect-PrivateDirectory $WorkRoot

try {
    $WrapperArchive = Join-Path $WorkRoot "wrapper-source.zip"
    $AgentArchive = Join-Path $WorkRoot "agent-source.zip"
    $WrapperSource = Join-Path $WorkRoot "wrapper-source"
    $AgentSource = Join-Path $WorkRoot "agent-source"
    $PublishRoot = Join-Path $WorkRoot "publish\win-x64"
    [IO.Directory]::CreateDirectory($WrapperSource) | Out-Null
    [IO.Directory]::CreateDirectory($AgentSource) | Out-Null
    [IO.Directory]::CreateDirectory($PublishRoot) | Out-Null

    & git -C $RepoRoot archive --format=zip "--output=$WrapperArchive" $Tag
    Assert-ExitCode "Archive wrapper source"
    & git -C $AgentRepo archive --format=zip "--output=$AgentArchive" $Tag
    Assert-ExitCode "Archive agent source"
    $ExpectedWrapperFiles = @(Invoke-GitNulList $RepoRoot "ls-files -z")
    $ExpectedAgentFiles = @(Invoke-GitNulList $AgentRepo "ls-files -z")
    $WrapperArchiveHash = (Get-FileHash -LiteralPath $WrapperArchive -Algorithm SHA256).Hash
    $AgentArchiveHash = (Get-FileHash -LiteralPath $AgentArchive -Algorithm SHA256).Hash
    Assert-SafeZipArchive $WrapperArchive $WrapperSource $ExpectedWrapperFiles
    Assert-SafeZipArchive $AgentArchive $AgentSource $ExpectedAgentFiles
    Expand-Archive -LiteralPath $WrapperArchive -DestinationPath $WrapperSource
    Expand-Archive -LiteralPath $AgentArchive -DestinationPath $AgentSource
    if ((Get-FileHash -LiteralPath $WrapperArchive -Algorithm SHA256).Hash -ne $WrapperArchiveHash -or
        (Get-FileHash -LiteralPath $AgentArchive -Algorithm SHA256).Hash -ne $AgentArchiveHash) {
        throw "A release source archive changed between validation and extraction"
    }
    Assert-ExpandedZipMatchesArchive $WrapperArchive $WrapperSource
    Assert-ExpandedZipMatchesArchive $AgentArchive $AgentSource

    $AssetDirectory = Join-Path $WrapperSource "src\LabTetherAgent\Assets"
    [IO.Directory]::CreateDirectory($AssetDirectory) | Out-Null
    $ChildPath = Join-Path $AssetDirectory "labtether-agent.exe"
    $PriorGoEnvironment = [ordered]@{
        CGO_ENABLED = $env:CGO_ENABLED
        GOOS = $env:GOOS
        GOARCH = $env:GOARCH
        GOWORK = $env:GOWORK
    }
    try {
        $env:CGO_ENABLED = "0"
        $env:GOOS = "windows"
        $env:GOARCH = "amd64"
        $env:GOWORK = "off"
        Push-Location $AgentSource
        try {
            & go build -trimpath "-ldflags=-s -w -X main.version=$Tag" -o $ChildPath .\cmd\labtether-agent
            Assert-ExitCode "Build bundled agent core"
        }
        finally {
            Pop-Location
        }
    }
    finally {
        foreach ($Name in $PriorGoEnvironment.Keys) {
            $Prior = $PriorGoEnvironment[$Name]
            if ($null -eq $Prior) {
                Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
            }
            else {
                Set-Item "Env:$Name" $Prior
            }
        }
    }
    Set-Content -LiteralPath (Join-Path $AssetDirectory "AGENT_VERSION") -Value $Tag -Encoding ASCII

    $MsBuild = Resolve-MSBuild
    $TestProject = Join-Path $WrapperSource "tests\LabTetherAgent.Tests\LabTetherAgent.Tests.csproj"
    & $MsBuild $TestProject -restore -t:Build -p:Platform=x64 -p:Configuration=Release -p:EnableMsixTooling=false -p:WindowsPackageType=None -p:GenerateAppxPackageOnBuild=false -p:RequireBundledAgent=true -nologo -verbosity:minimal
    Assert-ExitCode "Build release tests"
    & dotnet test --no-build --no-restore $TestProject -p:Platform=x64 -c Release
    Assert-ExitCode "Run release tests"
    $Audit = @(& dotnet list $TestProject package --vulnerable --include-transitive 2>&1)
    Assert-ExitCode "Audit release packages"
    if (($Audit -join "`n") -match "has the following vulnerable packages") {
        throw "Release package audit reported vulnerable packages"
    }

    $Project = Join-Path $WrapperSource "src\LabTetherAgent\LabTetherAgent.csproj"
    & $MsBuild $Project -restore -t:Publish -p:Configuration=Release -p:RuntimeIdentifier=win-x64 -p:Platform=x64 -p:SelfContained=true -p:WindowsPackageType=None -p:EnableMsixTooling=true -p:GenerateAppxPackageOnBuild=false -p:RequireBundledAgent=true -p:PublishTrimmed=false -p:PublishReadyToRun=false "-p:Version=$($Versions.Product)" "-p:AssemblyVersion=$($Versions.Binary)" "-p:FileVersion=$($Versions.Binary)" "-p:InformationalVersion=$($Versions.Product)" -p:IncludeSourceRevisionInInformationalVersion=false "-p:AppxPackageVersion=$($Versions.Binary)" "-p:PublishDir=$PublishRoot\" -nologo -verbosity:minimal
    Assert-ExitCode "Publish unsigned Windows payload"

    foreach ($RelativePath in $AuthoredPayloads) {
        $Payload = Join-Path $PublishRoot $RelativePath
        if (-not (Test-Path -LiteralPath $Payload -PathType Leaf)) {
            throw "Published payload is missing a required LabTether-authored file"
        }
        if ((Get-AuthenticodeSignature -LiteralPath $Payload).Status -ne [Management.Automation.SignatureStatus]::NotSigned) {
            throw "Unsigned preparation unexpectedly produced a signed LabTether-authored file"
        }
    }
    $PublishedVersion = (Get-Content -LiteralPath (Join-Path $PublishRoot "AGENT_VERSION") -Raw).Trim()
    if ($PublishedVersion -ne $Tag) {
        throw "Published child version does not match the release tag"
    }
    $PublishedChild = Join-Path $PublishRoot "Assets\labtether-agent.exe"
    $Help = @(& $PublishedChild help)
    if ($LASTEXITCODE -ne 0 -or $Help.Count -eq 0 -or $Help[0].Trim() -ne "labtether-agent $Tag") {
        throw "Published child failed its version smoke test"
    }
    $Probe = Start-Process -FilePath (Join-Path $PublishRoot "LabTetherAgent.exe") -ArgumentList "--winui-runtime-probe" -WorkingDirectory $PublishRoot -PassThru
    if (-not $Probe.WaitForExit(30000)) {
        Stop-Process -Id $Probe.Id -Force -ErrorAction SilentlyContinue
        throw "Published wrapper runtime probe timed out"
    }
    $Probe.Refresh()
    if ($Probe.ExitCode -ne 0) {
        throw "Published wrapper runtime probe failed"
    }

    $PublishPrefix = [IO.Path]::GetFullPath($PublishRoot).TrimEnd('\') + '\'
    $PublishedFileRecords = @()
    foreach ($PublishedFile in @(Get-ChildItem -LiteralPath $PublishRoot -File -Recurse -Force | Sort-Object FullName)) {
        if (-not $PublishedFile.FullName.StartsWith($PublishPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Published file escaped the release payload root"
        }
        $PublishedFileRecords += [pscustomobject][ordered]@{
            path = $PublishedFile.FullName.Substring($PublishPrefix.Length).Replace('\', '/')
            size = $PublishedFile.Length
            sha256 = (Get-FileHash -LiteralPath $PublishedFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $NormalizedAuthoredPayloads = @($AuthoredPayloads | ForEach-Object { $_.Replace('\', '/') })
    $PayloadRecords = @($PublishedFileRecords | Where-Object {
        $NormalizedAuthoredPayloads -contains $_.path
    })
    if ($PayloadRecords.Count -ne 3) {
        throw "Published file manifest is missing a LabTether-authored payload"
    }
    $PublishedBytes = [long](@($PublishedFileRecords | Measure-Object -Property size -Sum).Sum)
    if ($PublishedFileRecords.Count -le 0 -or $PublishedFileRecords.Count -gt 2048 -or
        $PublishedBytes -le 0 -or $PublishedBytes -gt 536870912 -or
        @($PublishedFileRecords | Where-Object { [long]$_.size -gt 268435456 }).Count -ne 0) {
        throw "Published payload exceeds the release file-count or uncompressed-byte limits"
    }
    $Provenance = [ordered]@{
        schema_version = 1
        payload = "labtether-agent-win-x64-unsigned"
        tag = $Tag
        wrapper_commit = $WrapperCommit
        agent_commit = $AgentCommit
        authored_payloads = $PayloadRecords
        published_files = $PublishedFileRecords
        published_file_count = $PublishedFileRecords.Count
        published_bytes = $PublishedBytes
        prepared_at = [DateTimeOffset]::UtcNow.ToString("o")
    }
    $Provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PublishRoot "release-provenance.json") -Encoding UTF8

    $TemporaryArchive = Join-Path $WorkRoot $UnsignedArchiveName
    Compress-Archive -Path (Join-Path $PublishRoot "*") -DestinationPath $TemporaryArchive -CompressionLevel Optimal
    $UnsignedVerifyRoot = Join-Path $WorkRoot "unsigned-verification"
    [IO.Directory]::CreateDirectory($UnsignedVerifyRoot) | Out-Null
    $ExpectedUnsignedFiles = @($PublishedFileRecords | ForEach-Object { $_.path }) + @("release-provenance.json")
    $UnsignedArchiveHash = (Get-FileHash -LiteralPath $TemporaryArchive -Algorithm SHA256).Hash
    Assert-SafeZipArchive $TemporaryArchive $UnsignedVerifyRoot $ExpectedUnsignedFiles
    Expand-Archive -LiteralPath $TemporaryArchive -DestinationPath $UnsignedVerifyRoot
    if ((Get-FileHash -LiteralPath $TemporaryArchive -Algorithm SHA256).Hash -ne $UnsignedArchiveHash) {
        throw "Unsigned release archive changed between validation and re-extraction"
    }
    Assert-ExpandedZipMatchesArchive $TemporaryArchive $UnsignedVerifyRoot
    foreach ($PublishedRecord in $PublishedFileRecords) {
        $VerifiedPath = Join-Path $UnsignedVerifyRoot $PublishedRecord.path
        if ((Get-Item -LiteralPath $VerifiedPath).Length -ne [long]$PublishedRecord.size -or
            (Get-FileHash -LiteralPath $VerifiedPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$PublishedRecord.sha256) {
            throw "Re-extracted unsigned payload differs from its source provenance"
        }
    }
    foreach ($RelativePath in $AuthoredPayloads) {
        if ((Get-AuthenticodeSignature -LiteralPath (Join-Path $UnsignedVerifyRoot $RelativePath)).Status -ne [Management.Automation.SignatureStatus]::NotSigned) {
            throw "Re-extracted unsigned archive unexpectedly contains a signed authored payload"
        }
    }
    $FinalArchive = Join-Path $OutputDirectory $UnsignedArchiveName
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
        throw "Output directory changed before unsigned archive placement"
    }
    [IO.File]::Move($TemporaryArchive, $FinalArchive)
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -File -Force).Count -ne 1) {
        throw "Unsigned preparation output contains unexpected files"
    }
    "Unsigned Windows release preparation passed. Transfer only the unsigned archive to the Mac signing lane."
    "archive_sha256=$((Get-FileHash -LiteralPath $FinalArchive -Algorithm SHA256).Hash.ToLowerInvariant())"
}
finally {
    if (Test-Path -LiteralPath $WorkRoot) {
        Assert-ExternalPath $WorkRoot
        Remove-Item -LiteralPath $WorkRoot -Recurse -Force
    }
}
