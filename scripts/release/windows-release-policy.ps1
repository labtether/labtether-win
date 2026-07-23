Set-StrictMode -Version Latest

$script:MaximumReleaseArchiveEntries = 2048
$script:MaximumReleaseArchiveBytes = 536870912
$script:MaximumReleaseArchiveEntryBytes = 268435456

function Test-ReleaseJsonInteger([object]$Value) {
    return ($Value -is [int] -or $Value -is [long])
}

function Invoke-GitNulList([string]$Repository, [string]$Arguments) {
    $StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $StartInfo.FileName = "git.exe"
    $StartInfo.Arguments = $Arguments
    $StartInfo.WorkingDirectory = $Repository
    $StartInfo.UseShellExecute = $false
    $StartInfo.CreateNoWindow = $true
    $StartInfo.RedirectStandardOutput = $true
    $StartInfo.RedirectStandardError = $true
    $Process = [Diagnostics.Process]::new()
    $Process.StartInfo = $StartInfo
    try {
        if (-not $Process.Start()) {
            throw "Unable to start git while inspecting tracked release source"
        }
        $Output = $Process.StandardOutput.ReadToEnd()
        $ErrorOutput = $Process.StandardError.ReadToEnd()
        $Process.WaitForExit()
        if ($Process.ExitCode -ne 0) {
            throw "Unable to inspect tracked release source"
        }
        return @($Output.Split([char[]]@([char]0), [StringSplitOptions]::RemoveEmptyEntries))
    }
    finally {
        $Process.Dispose()
    }
}

function Assert-TrackedSourcePolicy([string]$Repository) {
    foreach ($IndexEntry in @(Invoke-GitNulList $Repository "ls-files -s -z")) {
        $Tab = $IndexEntry.IndexOf([char]9)
        if ($Tab -lt 0) {
            throw "Unable to parse the tracked release index"
        }
        $Header = $IndexEntry.Substring(0, $Tab)
        $TrackedPath = $IndexEntry.Substring($Tab + 1)
        $Mode = $Header.Split(' ')[0]
        if ($Mode -notin @("100644", "100755")) {
            throw "Release source contains a non-regular tracked entry"
        }
        if ($TrackedPath -match '(?i)\.(pfx|p12|pkcs12|cer|crt|der|pem|key|jks|keystore|kdb|ppk)$') {
            throw "Release source contains a forbidden certificate or key filename"
        }
    }
    $PemPattern = ('-----BE' + 'GIN ([A-Z0-9 ]*PRIVATE KEY|CERTIFICATE)-----')
    $LocalSecretPath = ('Development' + '/certificates')
    $ContentPattern = "($PemPattern|(^|[/~])$LocalSecretPath([/]|$))"
    & git -C $Repository grep -I -q -E -- $ContentPattern -- . *> $null
    $ScanStatus = $LASTEXITCODE
    if ($ScanStatus -eq 0) {
        throw "Release source contains forbidden certificate, key, or local secret-path content"
    }
    if ($ScanStatus -ne 1) {
        throw "Unable to scan tracked release source"
    }
}

function Get-SafeReleasePath([string]$Name, [bool]$IsDirectory) {
    if ([string]::IsNullOrWhiteSpace($Name) -or
        [Text.Encoding]::UTF8.GetByteCount($Name) -gt 4096 -or
        $Name.Contains('\') -or
        $Name.StartsWith('/') -or
        $Name -match '\A[A-Za-z]:') {
        throw "Release archive contains an absolute, drive-letter, backslash, empty, or oversized path"
    }
    if ($IsDirectory) {
        if (-not $Name.EndsWith('/')) {
            throw "Release archive directory type conflicts with its path"
        }
        $RelativePath = $Name.Substring(0, $Name.Length - 1)
    }
    else {
        if ($Name.EndsWith('/')) {
            throw "Release archive file type conflicts with its path"
        }
        $RelativePath = $Name
    }
    $Segments = @($RelativePath.Split([char[]]@('/'), [StringSplitOptions]::None))
    if ([string]::IsNullOrEmpty($RelativePath) -or
        @($Segments | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0) {
        throw "Release archive contains an empty, dot, or traversal path segment"
    }
    $Reserved = @('CON', 'PRN', 'AUX', 'NUL', 'COM1', 'COM2', 'COM3', 'COM4', 'COM5', 'COM6', 'COM7', 'COM8', 'COM9', 'LPT1', 'LPT2', 'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9')
    $InvalidCharacters = [IO.Path]::GetInvalidFileNameChars()
    foreach ($Segment in $Segments) {
        if ([Text.Encoding]::UTF8.GetByteCount($Segment) -gt 255 -or
            $Segment.IndexOfAny($InvalidCharacters) -ge 0 -or
            $Segment.EndsWith(' ') -or
            $Segment.EndsWith('.')) {
            throw "Release archive contains a path Windows would normalize or reject"
        }
        $BaseName = $Segment.Split('.')[0]
        if ($Reserved -contains $BaseName) {
            throw "Release archive contains a reserved Windows path segment"
        }
    }
    return [pscustomobject]@{
        Path = $RelativePath
        CollisionKey = $RelativePath.Normalize([Text.NormalizationForm]::FormC)
    }
}

function Assert-SafeReleaseFilePath([string]$Path) {
    return (Get-SafeReleasePath $Path $false).Path
}

function Assert-SafeZipArchive(
    [string]$Path,
    [string]$DestinationRoot,
    [string[]]$ExpectedFiles = @(),
    [int]$MaximumEntries = $script:MaximumReleaseArchiveEntries,
    [long]$MaximumUncompressedBytes = $script:MaximumReleaseArchiveBytes,
    [long]$MaximumEntryBytes = $script:MaximumReleaseArchiveEntryBytes
) {
    if ($MaximumEntries -le 0 -or $MaximumUncompressedBytes -le 0 -or $MaximumEntryBytes -le 0) {
        throw "Release archive bounds must be positive"
    }
    $Root = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\') + '\'
    $Seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $SeenFolded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $Expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $ExpectedFolded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $ExpectedDirectories = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($ExpectedFile in $ExpectedFiles) {
        $NormalizedExpected = $ExpectedFile.Replace('\', '/')
        $SafeExpected = Assert-SafeReleaseFilePath $NormalizedExpected
        if (-not $Expected.Add($SafeExpected) -or -not $ExpectedFolded.Add($SafeExpected)) {
            throw "Expected release file set contains duplicate or case-colliding paths"
        }
        $Parts = @($SafeExpected.Split('/'))
        for ($Index = 1; $Index -lt $Parts.Count; $Index++) {
            [void]$ExpectedDirectories.Add((($Parts[0..($Index - 1)] -join '/') + '/'))
        }
    }
    $ArchiveFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $Nodes = [Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::OrdinalIgnoreCase)
    $Archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        if ($Archive.Entries.Count -le 0 -or $Archive.Entries.Count -gt $MaximumEntries) {
            throw "Release archive entry count is outside the release limit"
        }
        [long]$TotalUncompressedBytes = 0
        foreach ($Entry in $Archive.Entries) {
            if ($Entry.Length -lt 0 -or $Entry.Length -gt $MaximumEntryBytes -or
                $TotalUncompressedBytes -gt ($MaximumUncompressedBytes - $Entry.Length)) {
                throw "Release archive exceeds an uncompressed size limit"
            }
            $TotalUncompressedBytes += $Entry.Length
            $Name = $Entry.FullName
            $IsDirectory = $Name.EndsWith('/')
            $SafePath = Get-SafeReleasePath $Name $IsDirectory
            if (-not $Seen.Add($Name) -or -not $SeenFolded.Add($SafePath.CollisionKey)) {
                throw "Release archive contains a duplicate or normalized-colliding path"
            }
            $Nodes[$SafePath.CollisionKey] = $IsDirectory

            $ExternalAttributes = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$Entry.ExternalAttributes), 0)
            $UnixFileType = (($ExternalAttributes -shr 16) -band 0xF000)
            if ($UnixFileType -notin @(0, 0x4000, 0x8000) -or ($ExternalAttributes -band 0x400) -ne 0) {
                throw "Release archive contains a symbolic link, reparse point, or special entry"
            }
            if (($IsDirectory -and $UnixFileType -eq 0x8000) -or
                (-not $IsDirectory -and $UnixFileType -eq 0x4000) -or
                ($IsDirectory -and $Entry.Length -ne 0)) {
                throw "Release archive entry type conflicts with its path"
            }
            if ($Expected.Count -ne 0) {
                if ($IsDirectory) {
                    if (-not $ExpectedDirectories.Contains($Name)) {
                        throw "Release archive contains an unexpected directory entry"
                    }
                }
                else {
                    if (-not $Expected.Contains($Name)) {
                        throw "Release archive contains an unexpected file entry"
                    }
                    [void]$ArchiveFiles.Add($Name)
                }
            }
            $Destination = [IO.Path]::GetFullPath((Join-Path $DestinationRoot $SafePath.Path))
            if (-not $Destination.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Release archive path escapes the extraction root"
            }
        }
        foreach ($NodePath in @($Nodes.Keys)) {
            $Parts = @($NodePath.Split('/'))
            for ($Index = 1; $Index -lt $Parts.Count; $Index++) {
                $ParentPath = $Parts[0..($Index - 1)] -join '/'
                if ($Nodes.ContainsKey($ParentPath) -and -not $Nodes[$ParentPath]) {
                    throw "Release archive maps a file and descendant to the same path"
                }
            }
        }
        if ($Expected.Count -ne 0 -and
            ($ArchiveFiles.Count -ne $Expected.Count -or
             @($Expected | Where-Object { -not $ArchiveFiles.Contains($_) }).Count -ne 0)) {
            throw "Release archive does not contain the exact expected file set"
        }
    }
    finally {
        $Archive.Dispose()
    }
}

function Assert-ExpandedZipMatchesArchive([string]$Path, [string]$DestinationRoot) {
    $ArchiveRecords = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $Archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($Entry in $Archive.Entries) {
            if ($Entry.FullName.EndsWith('/')) {
                continue
            }
            $Stream = $Entry.Open()
            $Hasher = [Security.Cryptography.SHA256]::Create()
            try {
                $Hash = ([BitConverter]::ToString($Hasher.ComputeHash($Stream))).Replace('-', '').ToLowerInvariant()
            }
            finally {
                $Hasher.Dispose()
                $Stream.Dispose()
            }
            $ArchiveRecords.Add($Entry.FullName, [pscustomobject]@{ Size = [long]$Entry.Length; Hash = $Hash })
        }
    }
    finally {
        $Archive.Dispose()
    }

    $Root = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\') + '\'
    $ExtractedItems = @(Get-ChildItem -LiteralPath $DestinationRoot -Recurse -Force)
    if (@($ExtractedItems | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    }).Count -ne 0) {
        throw "Expanded release source contains a reparse point"
    }
    $ExtractedFiles = @($ExtractedItems | Where-Object { -not $_.PSIsContainer })
    if ($ExtractedFiles.Count -ne $ArchiveRecords.Count) {
        throw "Expanded release source does not contain the exact archive file count"
    }
    foreach ($File in $ExtractedFiles) {
        if (-not $File.FullName.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Expanded release source escaped its extraction root"
        }
        $RelativePath = $File.FullName.Substring($Root.Length).Replace('\', '/')
        if (-not $ArchiveRecords.ContainsKey($RelativePath)) {
            throw "Expanded release source contains an unexpected file"
        }
        $Expected = $ArchiveRecords[$RelativePath]
        if ($File.Length -ne $Expected.Size -or
            (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Expected.Hash) {
            throw "Expanded release source bytes differ from the validated archive"
        }
    }
}
