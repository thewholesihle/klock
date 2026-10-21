<#
.SYNOPSIS
  Verifies an uninstall left nothing behind. Run it after uninstalling Klock.
.PARAMETER IncludeUserData
  Also require the per-user settings and logs to be gone (use when you answered "Yes" to removing settings).
#>
param([switch]$IncludeUserData)

$problems = @()
$sys = [Environment]::GetFolderPath('System')

function Check($what, $present) {
    if ($present) { $script:problems += $what; Write-Host "  LEFTOVER  $what" -ForegroundColor Red }
    else { Write-Host "  ok        $what is gone" -ForegroundColor DarkGreen }
}

Write-Host 'Program files'
Check "$sys\Klock.scr" (Test-Path "$sys\Klock.scr")
foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if ($pf) { Check "$pf\Klock" (Test-Path "$pf\Klock") }
}
Check 'Start Menu shortcut' ((Test-Path "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Klock Settings.lnk") -or (Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Klock Settings.lnk"))

Write-Host 'Registry'
Check 'HKLM\Software\Klock' (Test-Path 'HKLM:\Software\Klock')
Check 'Uninstall entry' ((Get-ChildItem 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -like '*B6C1F1A0-7E52-4B5E-9C0E-4B1E5C0A6B11*' }) -ne $null)

Write-Host 'Active screen saver'
$active = (Get-ItemProperty 'HKCU:\Control Panel\Desktop' -ErrorAction SilentlyContinue).'SCRNSAVE.EXE'
Check 'SCRNSAVE.EXE still pointing at Klock' ($active -and $active -like '*Klock.scr')

Write-Host 'Processes'
Check 'running Klock processes' ((Get-Process -Name 'Klock', 'Klock.Settings' -ErrorAction SilentlyContinue) -ne $null)

if ($IncludeUserData) {
    Write-Host 'User data'
    Check 'HKCU\Software\Klock' (Test-Path 'HKCU:\Software\Klock')
    Check "$env:LOCALAPPDATA\Klock" (Test-Path "$env:LOCALAPPDATA\Klock")
}

if ($problems.Count -eq 0) { Write-Host "`nClean: nothing left behind." -ForegroundColor Green; exit 0 }
Write-Host "`n$($problems.Count) leftover(s) found." -ForegroundColor Red
exit 1
