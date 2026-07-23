[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')][string]$Tag,
    [Parameter(Mandatory)][string]$AgentRepo,
    [Parameter(Mandatory)][string]$AssetsDirectory,
    [Parameter(Mandatory)][string]$OutputProofPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
. (Join-Path $PSScriptRoot "windows-release-policy.ps1")

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$AgentRepo = (Resolve-Path $AgentRepo).Path
$AssetsDirectory = (Resolve-Path $AssetsDirectory).Path
$OutputProofPath = [IO.Path]::GetFullPath($OutputProofPath)
$ArchiveName = "labtether-agent-win-x64.zip"
$ChecksumName = "labtether-agent-win-x64.zip.sha256"
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
        throw "Release verification files must remain outside source repositories"
    }
}

function Protect-PrivateDirectory([string]$Path) {
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    $Item = Get-Item -LiteralPath $Path -Force
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Private verification directory must not be a reparse point"
    }
    $Sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls.exe $Path /inheritance:r /grant:r "$Sid`:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to apply a current-user-only ACL to release verification staging"
    }
}

function Read-ZipJsonEntry([string]$Path, [string]$EntryName, [long]$MaximumBytes) {
    $Archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $Matches = @($Archive.Entries | Where-Object { $_.FullName -ceq $EntryName })
        if ($Matches.Count -ne 1 -or $Matches[0].Length -le 0 -or $Matches[0].Length -gt $MaximumBytes) {
            throw "Release archive metadata entry is missing or exceeds its size limit"
        }
        $Stream = $Matches[0].Open()
        $Reader = [IO.StreamReader]::new($Stream, [Text.UTF8Encoding]::new($false, $true), $true)
        try {
            return ($Reader.ReadToEnd() | ConvertFrom-Json)
        }
        finally {
            $Reader.Dispose()
            $Stream.Dispose()
        }
    }
    finally {
        $Archive.Dispose()
    }
}

$WrapperCommit = Assert-CleanTaggedSource $RepoRoot $Tag
$AgentCommit = Assert-CleanTaggedSource $AgentRepo $Tag
Assert-TrackedSourcePolicy $RepoRoot
Assert-TrackedSourcePolicy $AgentRepo
Assert-ExternalPath $AssetsDirectory
Assert-ExternalPath $OutputProofPath

$AssetItems = @(Get-ChildItem -LiteralPath $AssetsDirectory -Force)
if ($AssetItems.Count -ne 2 -or
    @($AssetItems | Where-Object { $_.Name -eq $ArchiveName -and -not $_.PSIsContainer }).Count -ne 1 -or
    @($AssetItems | Where-Object { $_.Name -eq $ChecksumName -and -not $_.PSIsContainer }).Count -ne 1) {
    throw "Release assets directory must contain exactly the two allowed files"
}
if (@($AssetItems | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -ne 0) {
    throw "Release assets must not be reparse points"
}

$ArchivePath = Join-Path $AssetsDirectory $ArchiveName
$ChecksumPath = Join-Path $AssetsDirectory $ChecksumName
$ChecksumText = Get-Content -LiteralPath $ChecksumPath -Raw
$ChecksumMatch = [regex]::Match($ChecksumText, "\A([0-9A-Fa-f]{64})  labtether-agent-win-x64\.zip\r?\n?\z")
if (-not $ChecksumMatch.Success) {
    throw "Release checksum asset has an invalid format"
}
$ArchiveHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ArchiveHash -ne $ChecksumMatch.Groups[1].Value.ToLowerInvariant()) {
    throw "Release archive checksum mismatch"
}

$ProofParent = Split-Path $OutputProofPath -Parent
Assert-ExternalPath $ProofParent
if (Test-Path -LiteralPath $ProofParent) {
    $ProofParentItem = Get-Item -LiteralPath $ProofParent -Force
    if (-not $ProofParentItem.PSIsContainer -or
        ($ProofParentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        @(Get-ChildItem -LiteralPath $ProofParent -Force).Count -ne 0) {
        throw "Output proof directory must be an empty non-reparse directory"
    }
}
Protect-PrivateDirectory $ProofParent
if (Test-Path -LiteralPath $OutputProofPath) {
    throw "Output proof path already exists"
}

$WorkRoot = Join-Path ([IO.Path]::GetTempPath()) "labtether-win-verify-$([Guid]::NewGuid().ToString('N'))"
Assert-ExternalPath $WorkRoot
Protect-PrivateDirectory $WorkRoot

try {
    Assert-SafeZipArchive $ArchivePath $WorkRoot
    $Provenance = Read-ZipJsonEntry $ArchivePath "release-provenance.json" 16777216
    if ($Provenance.schema_version -ne 1 -or
        $Provenance.payload -ne "labtether-agent-win-x64-unsigned" -or
        $Provenance.tag -ne $Tag -or
        $Provenance.wrapper_commit -ne $WrapperCommit -or
        $Provenance.agent_commit -ne $AgentCommit) {
        throw "Release provenance does not match clean tagged sources"
    }
    $AuthoredRecords = @($Provenance.authored_payloads)
    if ($AuthoredRecords.Count -ne 3) {
        throw "Release provenance contains an unexpected authored payload count"
    }
    $ProvenancePaths = @($AuthoredRecords | ForEach-Object { Assert-SafeReleaseFilePath ([string]$_.path) } | Sort-Object)
    $ExpectedProvenancePaths = @($AuthoredPayloads | ForEach-Object { $_.Replace('\', '/') } | Sort-Object)
    if (($ProvenancePaths -join "`n") -cne ($ExpectedProvenancePaths -join "`n")) {
        throw "Release provenance contains an unexpected authored payload"
    }

    $PublishedRecords = @($Provenance.published_files)
    if (-not (Test-ReleaseJsonInteger $Provenance.published_file_count) -or
        -not (Test-ReleaseJsonInteger $Provenance.published_bytes) -or
        $PublishedRecords.Count -ne [int]$Provenance.published_file_count -or
        $PublishedRecords.Count -le 0 -or $PublishedRecords.Count -gt 2048) {
        throw "Release provenance published file count is inconsistent"
    }
    $PublishedByPath = @{}
    [long]$PublishedBytes = 0
    foreach ($Record in $PublishedRecords) {
        $Path = Assert-SafeReleaseFilePath ([string]$Record.path)
        if ($Path -cin @("release-provenance.json", "signed-payloads.json") -or
            $PublishedByPath.ContainsKey($Path) -or
            -not (Test-ReleaseJsonInteger $Record.size) -or
            [long]$Record.size -lt 0 -or
            [long]$Record.size -gt 268435456 -or
            [string]$Record.sha256 -notmatch '\A[0-9a-f]{64}\z') {
            throw "Release provenance contains an unsafe or invalid published file record"
        }
        $PublishedByPath[$Path] = $Record
        if ($PublishedBytes -gt (536870912 - [long]$Record.size)) {
            throw "Release provenance exceeds the uncompressed byte limit"
        }
        $PublishedBytes += [long]$Record.size
    }
    if ($PublishedBytes -ne [long]$Provenance.published_bytes) {
        throw "Release provenance published byte count is inconsistent"
    }
    foreach ($AuthoredRecord in $AuthoredRecords) {
        $Path = Assert-SafeReleaseFilePath ([string]$AuthoredRecord.path)
        if (-not $PublishedByPath.ContainsKey($Path) -or
            -not (Test-ReleaseJsonInteger $AuthoredRecord.size) -or
            [long]$AuthoredRecord.size -lt 0 -or
            [string]$AuthoredRecord.sha256 -notmatch '\A[0-9a-f]{64}\z' -or
            [long]$PublishedByPath[$Path].size -ne [long]$AuthoredRecord.size -or
            [string]$PublishedByPath[$Path].sha256 -cne [string]$AuthoredRecord.sha256) {
            throw "Authored payload provenance differs from the published file manifest"
        }
    }

    $SignedManifest = Read-ZipJsonEntry $ArchivePath "signed-payloads.json" 1048576
    $SignedRecords = @($SignedManifest.payloads)
    if ($SignedManifest.schema_version -ne 1 -or $SignedRecords.Count -ne 3) {
        throw "Signed payload manifest is invalid"
    }
    $SignedByPath = @{}
    foreach ($Record in $SignedRecords) {
        $Path = Assert-SafeReleaseFilePath ([string]$Record.path)
        if ($ExpectedProvenancePaths -cnotcontains $Path -or
            $SignedByPath.ContainsKey($Path) -or
            -not (Test-ReleaseJsonInteger $Record.size) -or
            [long]$Record.size -le 0 -or
            [string]$Record.sha256 -notmatch '\A[0-9a-f]{64}\z') {
            throw "Signed payload manifest contains an unexpected record"
        }
        $SignedByPath[$Path] = $Record
    }
    if ($SignedByPath.Count -ne 3) {
        throw "Signed payload manifest is incomplete"
    }

    $ExpectedArchiveFiles = @($PublishedByPath.Keys) + @("release-provenance.json", "signed-payloads.json")
    Assert-SafeZipArchive $ArchivePath $WorkRoot $ExpectedArchiveFiles
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $WorkRoot
    if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $ArchiveHash) {
        throw "Release archive changed between validation and extraction"
    }
    $ExtractedItems = @(Get-ChildItem -LiteralPath $WorkRoot -Recurse -Force)
    if (@($ExtractedItems | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    }).Count -ne 0) {
        throw "Release archive contains a reparse point"
    }
    $WorkPrefix = [IO.Path]::GetFullPath($WorkRoot).TrimEnd('\') + '\'
    $ExtractedFiles = @($ExtractedItems | Where-Object { -not $_.PSIsContainer })
    if ($ExtractedFiles.Count -ne $ExpectedArchiveFiles.Count) {
        throw "Re-extracted release contains an unexpected file count"
    }
    $ExtractedPaths = @($ExtractedFiles | ForEach-Object {
        if (-not $_.FullName.StartsWith($WorkPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Re-extracted file escaped the verification root"
        }
        $_.FullName.Substring($WorkPrefix.Length).Replace('\', '/')
    } | Sort-Object)
    if (($ExtractedPaths -join "`n") -cne (@($ExpectedArchiveFiles | Sort-Object) -join "`n")) {
        throw "Re-extracted release does not contain the exact expected file set"
    }

    foreach ($Record in $PublishedRecords) {
        $Path = [string]$Record.path
        if ($ExpectedProvenancePaths -ccontains $Path) {
            continue
        }
        $Payload = Join-Path $WorkRoot $Path
        if ((Get-Item -LiteralPath $Payload).Length -ne [long]$Record.size -or
            (Get-FileHash -LiteralPath $Payload -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$Record.sha256) {
            throw "A non-authored release file differs from unsigned provenance"
        }
    }

    $SignerThumbprint = $null
    foreach ($RelativePath in $AuthoredPayloads) {
        $ManifestPath = $RelativePath.Replace('\', '/')
        $SignedRecord = $SignedByPath[$ManifestPath]
        $Payload = Join-Path $WorkRoot $RelativePath
        if (-not (Test-Path -LiteralPath $Payload -PathType Leaf)) {
            throw "Release archive is missing a required signed payload"
        }
        $ActualPayloadHash = (Get-FileHash -LiteralPath $Payload -Algorithm SHA256).Hash.ToLowerInvariant()
        if ((Get-Item -LiteralPath $Payload).Length -ne [long]$SignedRecord.size -or
            $ActualPayloadHash -cne [string]$SignedRecord.sha256) {
            throw "Signed payload size or checksum differs from its manifest"
        }
        $Signature = Get-AuthenticodeSignature -LiteralPath $Payload
        if ($Signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
            $null -eq $Signature.SignerCertificate -or
            $null -eq $Signature.TimeStamperCertificate) {
            throw "A required payload does not have a valid timestamped Authenticode signature"
        }
        if ($null -eq $SignerThumbprint) {
            $SignerThumbprint = $Signature.SignerCertificate.Thumbprint
        }
        elseif ($SignerThumbprint -ne $Signature.SignerCertificate.Thumbprint) {
            throw "Release payloads were not signed by one certificate"
        }
    }
    $PublishedVersion = (Get-Content -LiteralPath (Join-Path $WorkRoot "AGENT_VERSION") -Raw).Trim()
    if ($PublishedVersion -ne $Tag) {
        throw "Bundled child version marker does not match the release tag"
    }
    $Child = Join-Path $WorkRoot "Assets\labtether-agent.exe"
    $Help = @(& $Child help)
    if ($LASTEXITCODE -ne 0 -or $Help.Count -eq 0 -or $Help[0].Trim() -ne "labtether-agent $Tag") {
        throw "Signed bundled child failed its version smoke test"
    }
    $Wrapper = Join-Path $WorkRoot "LabTetherAgent.exe"
    $Probe = Start-Process -FilePath $Wrapper -ArgumentList "--winui-runtime-probe" -WorkingDirectory $WorkRoot -PassThru
    if (-not $Probe.WaitForExit(30000)) {
        Stop-Process -Id $Probe.Id -Force -ErrorAction SilentlyContinue
        throw "Signed wrapper runtime probe timed out"
    }
    $Probe.Refresh()
    if ($Probe.ExitCode -ne 0) {
        throw "Signed wrapper runtime probe failed"
    }

    $Proof = [ordered]@{
        schema_version = 1
        status = "signed-windows-verification-pass"
        tag = $Tag
        wrapper_commit = $WrapperCommit
        agent_commit = $AgentCommit
        archive_sha256 = $ArchiveHash
        verified_payload_count = 3
        payloads = @($SignedRecords | Sort-Object path)
        verified_at = [DateTimeOffset]::UtcNow.ToString("o")
    }
    $ProofJson = $Proof | ConvertTo-Json -Depth 6
    $ProofEncoding = [Text.UTF8Encoding]::new($false)
    $ProofStream = [IO.File]::Open($OutputProofPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $ProofBytes = $ProofEncoding.GetBytes($ProofJson + [Environment]::NewLine)
        $ProofStream.Write($ProofBytes, 0, $ProofBytes.Length)
        $ProofStream.Flush($true)
    }
    finally {
        $ProofStream.Dispose()
    }
    "Signed Windows release verification passed."
    "archive_sha256=$ArchiveHash"
}
finally {
    if (Test-Path -LiteralPath $WorkRoot) {
        Assert-ExternalPath $WorkRoot
        Remove-Item -LiteralPath $WorkRoot -Recurse -Force
    }
}
