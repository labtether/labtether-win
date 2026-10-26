[CmdletBinding()]
param([Parameter(Mandatory)][string]$MsiPath)

# WiX copies each versioned file's language list from its PE version
# resource into File.Language, and WiX v5 has no switch to skip that file
# info. ICE03 checks those values against its own fixed language table:
# multi-language lists overflow the column, and even single ids that Windows
# itself supports (newer locales) are rejected as Invalid Language Id.
# Keep a file's language only when it is neutral (0) or the package language
# (1033, Package/@Language in Package.wxs); record every other payload file
# as neutral before ICE validation runs, so ICE03 still checks every table.
# File.Language only breaks version ties against an existing file at the same
# path; this per-user package owns its folder and MajorUpgrade removes the
# previous version first.
# Runs in Windows PowerShell; the Windows Installer COM object needs late
# binding there.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$KeptLanguages = @("0", "1033")

function Invoke-Msi($Target, [string]$Member, [object[]]$Arguments = @(), [string]$Kind = "InvokeMethod") {
    return $Target.GetType().InvokeMember($Member, [Reflection.BindingFlags]$Kind, $null, $Target, $Arguments)
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = Invoke-Msi $installer "OpenDatabase" @([IO.Path]::GetFullPath($MsiPath), 1)
$view = Invoke-Msi $database "OpenView" @('SELECT `File`, `FileName`, `Language` FROM `File`')
Invoke-Msi $view "Execute" | Out-Null
$changes = @()
while ($null -ne ($record = Invoke-Msi $view "Fetch")) {
    $language = Invoke-Msi $record "StringData" @(3) "GetProperty"
    if ([string]::IsNullOrEmpty($language) -or $KeptLanguages -ccontains $language) { continue }
    $changes += [pscustomobject]@{
        Key = Invoke-Msi $record "StringData" @(1) "GetProperty"
        Name = Invoke-Msi $record "StringData" @(2) "GetProperty"
        Language = $language
    }
}
Invoke-Msi $view "Close" | Out-Null

foreach ($change in $changes) {
    if ($change.Key -notmatch '^[A-Za-z0-9_.]+$') {
        throw "Unexpected File table key: $($change.Key)"
    }
    $update = Invoke-Msi $database "OpenView" @("UPDATE ``File`` SET ``Language`` = '0' WHERE ``File`` = '$($change.Key)'")
    Invoke-Msi $update "Execute" | Out-Null
    Invoke-Msi $update "Close" | Out-Null
    Write-Output "File language '$($change.Language)' set to 0 (neutral): $($change.Name)"
}
Invoke-Msi $database "Commit" | Out-Null
foreach ($object in @($view, $database, $installer)) {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($object)
}
Write-Output "Normalized $($changes.Count) file language value(s) in $MsiPath."
