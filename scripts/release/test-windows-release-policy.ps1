[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
. (Join-Path $PSScriptRoot "windows-release-policy.ps1")

function Expect-Rejection([string]$Label, [scriptblock]$Action) {
    try {
        & $Action
    }
    catch {
        return
    }
    throw "Malicious release-policy fixture was accepted: $Label"
}

function New-FixtureArchive([string]$Path, [object[]]$Entries) {
    $Stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $Archive = [IO.Compression.ZipArchive]::new($Stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($Spec in $Entries) {
                $Entry = $Archive.CreateEntry([string]$Spec.name, [IO.Compression.CompressionLevel]::Optimal)
                if ($null -ne $Spec.external_attributes) {
                    $Entry.ExternalAttributes = [int]$Spec.external_attributes
                }
                if ($null -ne $Spec.content) {
                    $Writer = [IO.StreamWriter]::new($Entry.Open(), [Text.UTF8Encoding]::new($false))
                    try { $Writer.Write([string]$Spec.content) } finally { $Writer.Dispose() }
                }
            }
        }
        finally {
            $Archive.Dispose()
        }
    }
    finally {
        $Stream.Dispose()
    }
}

$Root = Join-Path ([IO.Path]::GetTempPath()) "labtether-release-policy-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($Root) | Out-Null
try {
    $SourceRepo = Join-Path $Root "source"
    [IO.Directory]::CreateDirectory((Join-Path $SourceRepo "scripts\release")) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "windows-release-policy.ps1") -Destination (Join-Path $SourceRepo "scripts\release\windows-release-policy.ps1")
    [IO.File]::WriteAllText((Join-Path $SourceRepo "normal.txt"), "clean source")
    & git -C $SourceRepo init -q
    & git -C $SourceRepo config core.autocrlf false
    & git -C $SourceRepo config user.name "Release Policy Fixture"
    & git -C $SourceRepo config user.email "release-policy@example.invalid"
    & git -C $SourceRepo add -- .
    & git -C $SourceRepo commit -qm "clean fixture"
    & git -C $SourceRepo tag v1.2.3
    Assert-TrackedSourcePolicy $SourceRepo

    $Commit = (& git -C $SourceRepo rev-parse HEAD).Trim()
    & git -C $SourceRepo update-index --add --cacheinfo "160000,$Commit,nested-repository"
    Expect-Rejection "non-regular index mode" { Assert-TrackedSourcePolicy $SourceRepo }
    & git -C $SourceRepo reset --hard -q HEAD

    [IO.File]::WriteAllText((Join-Path $SourceRepo "forbidden.PfX"), "fixture")
    & git -C $SourceRepo add -- "forbidden.PfX"
    Expect-Rejection "forbidden certificate filename" { Assert-TrackedSourcePolicy $SourceRepo }
    & git -C $SourceRepo reset --hard -q HEAD
    Remove-Item -LiteralPath (Join-Path $SourceRepo "forbidden.PfX") -Force -ErrorAction SilentlyContinue

    $Marker = ('-----BE' + 'GIN PRIVATE KEY-----')
    [IO.File]::WriteAllText((Join-Path $SourceRepo "forbidden-content.txt"), $Marker)
    & git -C $SourceRepo add -- "forbidden-content.txt"
    Expect-Rejection "forbidden key content" { Assert-TrackedSourcePolicy $SourceRepo }
    & git -C $SourceRepo reset --hard -q HEAD
    Remove-Item -LiteralPath (Join-Path $SourceRepo "forbidden-content.txt") -Force -ErrorAction SilentlyContinue

    $Destination = Join-Path $Root "destination"
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    $RegularMode = [int](0x81A4 -shl 16)
    $DirectoryMode = [int](0x41ED -shl 16)
    $SymlinkMode = [int](0xA1FF -shl 16)
    $FifoMode = [int](0x11A4 -shl 16)
    $Fixtures = [ordered]@{
        "parent traversal" = @(@{ name = "../escape"; content = "x"; external_attributes = $RegularMode })
        "absolute path" = @(@{ name = "/absolute"; content = "x"; external_attributes = $RegularMode })
        "drive-letter path" = @(@{ name = "C:/drive"; content = "x"; external_attributes = $RegularMode })
        "leading dot segment" = @(@{ name = "./leading"; content = "x"; external_attributes = $RegularMode })
        "interior dot segment" = @(@{ name = "a/./b"; content = "x"; external_attributes = $RegularMode })
        "repeated separator" = @(@{ name = "a//b"; content = "x"; external_attributes = $RegularMode })
        "backslash confusion" = @(@{ name = "a\b"; content = "x"; external_attributes = $RegularMode })
        "exact duplicate" = @(
            @{ name = "same"; content = "1"; external_attributes = $RegularMode },
            @{ name = "same"; content = "2"; external_attributes = $RegularMode }
        )
        "case-fold collision" = @(
            @{ name = "Payload.dll"; content = "1"; external_attributes = $RegularMode },
            @{ name = "payload.dll"; content = "2"; external_attributes = $RegularMode }
        )
        "Unicode normalization collision" = @(
            @{ name = "caf$([char]0x00e9)"; content = "1"; external_attributes = $RegularMode },
            @{ name = "cafe$([char]0x0301)"; content = "2"; external_attributes = $RegularMode }
        )
        "file and descendant" = @(
            @{ name = "Assets"; content = "1"; external_attributes = $RegularMode },
            @{ name = "Assets/agent.exe"; content = "2"; external_attributes = $RegularMode }
        )
        "reserved Windows name" = @(@{ name = "Assets/CON.txt"; content = "x"; external_attributes = $RegularMode })
        "trailing dot" = @(@{ name = "Assets/name."; content = "x"; external_attributes = $RegularMode })
        "symbolic link" = @(@{ name = "link"; content = "target"; external_attributes = $SymlinkMode })
        "special FIFO" = @(@{ name = "pipe"; content = ""; external_attributes = $FifoMode })
        "directory type mismatch" = @(@{ name = "folder/"; content = $null; external_attributes = $RegularMode })
    }
    foreach ($Fixture in $Fixtures.GetEnumerator()) {
        $ArchivePath = Join-Path $Root "$($Fixture.Key.Replace(' ', '-')).zip"
        New-FixtureArchive $ArchivePath @($Fixture.Value)
        Expect-Rejection $Fixture.Key { Assert-SafeZipArchive $ArchivePath $Destination }
    }

    $BoundedArchive = Join-Path $Root "bounded.zip"
    New-FixtureArchive $BoundedArchive @(
        @{ name = "one"; content = "11"; external_attributes = $RegularMode },
        @{ name = "two"; content = "22"; external_attributes = $RegularMode },
        @{ name = "three"; content = "33"; external_attributes = $RegularMode }
    )
    Expect-Rejection "entry count bound" { Assert-SafeZipArchive $BoundedArchive $Destination @() 2 100 100 }
    Expect-Rejection "per-entry byte bound" { Assert-SafeZipArchive $BoundedArchive $Destination @() 10 100 1 }
    Expect-Rejection "total byte bound" { Assert-SafeZipArchive $BoundedArchive $Destination @() 10 5 100 }

    $GoodArchive = Join-Path $Root "good.zip"
    New-FixtureArchive $GoodArchive @(
        @{ name = "Assets/"; content = $null; external_attributes = $DirectoryMode },
        @{ name = "Assets/agent.exe"; content = "agent"; external_attributes = $RegularMode },
        @{ name = "root.txt"; content = "root"; external_attributes = $RegularMode }
    )
    Assert-SafeZipArchive $GoodArchive $Destination @("Assets/agent.exe", "root.txt")

    "Windows release policy malicious-fixture tests passed."
}
finally {
    Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
}
