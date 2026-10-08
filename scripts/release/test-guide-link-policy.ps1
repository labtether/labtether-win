[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "windows-release-policy.ps1")

function Invoke-FixtureGit([string]$Repository, [string[]]$GitArguments) {
    & git -C $Repository @GitArguments *> $null
    if ($LASTEXITCODE -ne 0) { throw "Unable to prepare source-policy fixture" }
}

function Set-FixtureLink([string]$Repository, [string]$Target) {
    $Path = Join-Path $Repository "CLAUDE.md"
    Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    Push-Location $Repository
    try {
        New-Item -ItemType SymbolicLink -Path "CLAUDE.md" -Target $Target | Out-Null
    }
    finally { Pop-Location }
}

function New-GuideFixture {
    $Repository = Join-Path $Root ([Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($Repository) | Out-Null
    Invoke-FixtureGit $Repository @("init", "-q")
    Invoke-FixtureGit $Repository @("config", "core.autocrlf", "false")
    Invoke-FixtureGit $Repository @("config", "core.symlinks", "true")
    [IO.File]::WriteAllText((Join-Path $Repository "AGENTS.md"), "Shared project instructions`n")
    [IO.File]::WriteAllText((Join-Path $Repository "ordinary.txt"), "ordinary fixture`n")
    Set-FixtureLink $Repository "AGENTS.md"
    Invoke-FixtureGit $Repository @("add", "--", "AGENTS.md", "CLAUDE.md", "ordinary.txt")
    return $Repository
}

function Expect-GuideRejection([string]$Label, [string]$Repository) {
    try { Assert-TrackedSourcePolicy $Repository }
    catch { return }
    throw "Source policy accepted unsafe guide fixture: $Label"
}

$Root = Join-Path ([IO.Path]::GetTempPath()) "labtether-guide-policy-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($Root) | Out-Null
try {
    $Repo = New-GuideFixture
    Assert-TrackedSourcePolicy $Repo

    $Repo = New-GuideFixture
    Set-FixtureLink $Repo "ordinary.txt"
    Expect-GuideRejection "changed local target" $Repo
    Invoke-FixtureGit $Repo @("add", "--", "CLAUDE.md")
    Expect-GuideRejection "changed indexed target" $Repo

    $Repo = New-GuideFixture
    [IO.File]::WriteAllText((Join-Path $Repo "dummy.key"), "dummy fixture only")
    Set-FixtureLink $Repo "dummy.key"
    Invoke-FixtureGit $Repo @("add", "--", "CLAUDE.md")
    Expect-GuideRejection "secret-like target in disposable fixture" $Repo

    $Repo = New-GuideFixture
    $Outside = Join-Path $Root "outside.txt"
    [IO.File]::WriteAllText($Outside, "external fixture only")
    Set-FixtureLink $Repo $Outside
    Invoke-FixtureGit $Repo @("add", "--", "CLAUDE.md")
    Expect-GuideRejection "external target" $Repo

    $Repo = New-GuideFixture
    Remove-Item -LiteralPath (Join-Path $Repo "AGENTS.md") -Force
    Expect-GuideRejection "missing guide target" $Repo

    $Repo = New-GuideFixture
    Remove-Item -LiteralPath (Join-Path $Repo "AGENTS.md") -Force
    Push-Location $Repo
    try { New-Item -ItemType SymbolicLink -Path "AGENTS.md" -Target "ordinary.txt" | Out-Null }
    finally { Pop-Location }
    Expect-GuideRejection "symlinked guide target" $Repo

    $Repo = New-GuideFixture
    [IO.File]::AppendAllText((Join-Path $Repo "AGENTS.md"), "altered guide")
    Expect-GuideRejection "modified target bytes" $Repo

    $Repo = New-GuideFixture
    Invoke-FixtureGit $Repo @("rm", "--cached", "-q", "--", "AGENTS.md")
    Expect-GuideRejection "untracked guide target" $Repo

    $Repo = New-GuideFixture
    Remove-Item -LiteralPath (Join-Path $Repo "CLAUDE.md") -Force
    [IO.File]::WriteAllText((Join-Path $Repo "CLAUDE.md"), "AGENTS.md")
    Expect-GuideRejection "plain file instead of local symlink" $Repo

    $Repo = New-GuideFixture
    Push-Location $Repo
    try { New-Item -ItemType SymbolicLink -Path "other-link" -Target "ordinary.txt" | Out-Null }
    finally { Pop-Location }
    Invoke-FixtureGit $Repo @("add", "--", "other-link")
    Expect-GuideRejection "unrelated tracked link" $Repo

    $Repo = New-GuideFixture
    [IO.Directory]::CreateDirectory((Join-Path $Repo "nested")) | Out-Null
    Push-Location (Join-Path $Repo "nested")
    try { New-Item -ItemType SymbolicLink -Path "CLAUDE.md" -Target "../AGENTS.md" | Out-Null }
    finally { Pop-Location }
    Invoke-FixtureGit $Repo @("add", "--", "nested/CLAUDE.md")
    Expect-GuideRejection "nested guide link" $Repo

    $Repo = New-GuideFixture
    $Hash = (@(Invoke-GitNulList $Repo "hash-object -w --stdin" "AGENTS.md`n")[0]).Trim()
    Invoke-FixtureGit $Repo @("update-index", "--cacheinfo", "120000,$Hash,CLAUDE.md")
    Expect-GuideRejection "non-exact indexed link bytes" $Repo

    $Repo = New-GuideFixture
    $Hash = (@(Invoke-GitNulList $Repo "hash-object -w --stdin" "AGENTS.md")[0]).Trim()
    Invoke-FixtureGit $Repo @("update-index", "--cacheinfo", "120000,$Hash,AGENTS.md")
    Expect-GuideRejection "non-regular target in index" $Repo

    "Canonical guide link policy fixtures passed."
}
finally {
    Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
}
