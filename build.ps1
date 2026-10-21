<#
.SYNOPSIS
  Builds Klock for Windows: tests, self-contained single-file x64/ARM64 binaries, optional signing, installer.

.EXAMPLE
  ./build.ps1                       # tests + x64 and arm64 + installer (if Inno Setup is installed)
  ./build.ps1 -Arch x64 -SkipTests  # quick x64-only build
  ./build.ps1 -Sign                 # sign with the certificate in KLOCK_SIGN_PFX / KLOCK_SIGN_PASSWORD

.NOTES
  Signing is a placeholder hook: no certificate ships with the repo. Set:
    KLOCK_SIGN_PFX        path to your code-signing .pfx
    KLOCK_SIGN_PASSWORD   its password
    KLOCK_TIMESTAMP_URL   optional (default: http://timestamp.digicert.com)
  The installer needs Inno Setup 6 (https://jrsoftware.org/isinfo.php). Set INNO_SETUP_PATH to ISCC.exe if it is
  not in the default location.
#>
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    # Accepts -Arch x64,arm64 from PowerShell and "x64,arm64" from `powershell -File` (which cannot pass arrays).
    [string[]]$Arch = @('x64', 'arm64'),
    [switch]$Sign,
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$Arch = @($Arch | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
foreach ($a in $Arch) { if ($a -notin 'x64', 'arm64') { throw "Unknown architecture '$a' (use x64 and/or arm64)." } }
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
Set-Location $root

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kits) {
        return Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}

function Invoke-Sign([string[]]$files) {
    if (-not $Sign) { Write-Host '   (unsigned: pass -Sign with KLOCK_SIGN_PFX to sign)' -ForegroundColor DarkGray; return }
    $pfx = $env:KLOCK_SIGN_PFX
    if (-not $pfx -or -not (Test-Path $pfx)) { throw 'KLOCK_SIGN_PFX is not set to an existing .pfx file.' }
    $tool = Find-SignTool
    if (-not $tool) { throw 'signtool.exe not found. Install the Windows SDK.' }
    $ts = if ($env:KLOCK_TIMESTAMP_URL) { $env:KLOCK_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
    foreach ($f in $files) {
        & $tool sign /fd SHA256 /td SHA256 /tr $ts /f $pfx /p $env:KLOCK_SIGN_PASSWORD $f | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "signing failed: $f" }
        Write-Host "   signed $(Split-Path $f -Leaf)"
    }
}

if ($Clean -and (Test-Path $dist)) {
    # Fresh 70 MB executables are often held briefly by OneDrive sync or an antivirus scan: retry before giving up.
    for ($try = 1; $try -le 6; $try++) {
        try { Remove-Item $dist -Recurse -Force -ErrorAction Stop; break }
        catch {
            if ($try -eq 6) { throw "Could not clean '$dist' (something is holding a file open): $($_.Exception.Message)" }
            Start-Sleep -Seconds 2
        }
    }
}
New-Item -ItemType Directory -Force $dist | Out-Null

# Regenerate localised resources from tools/strings so a missing key can never ship.
Step 'Generating string resources'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\gen-resx.ps1')
if ($LASTEXITCODE -ne 0) { throw 'resource generation failed' }

if (-not $SkipTests) {
    Step 'Running tests'
    dotnet test (Join-Path $root 'tests\Klock.Tests') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
}

$common = @('-c', 'Release', '--self-contained', 'true', '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none', '-p:DebugSymbols=false',
            "-p:Version=$Version", '--nologo')

foreach ($a in $Arch) {
    $rid = "win-$a"
    $out = Join-Path $dist $a

    Step "Publishing screensaver ($rid)"
    # Uncompressed on purpose: a compressed bundle is inflated into RAM at launch (~50 MB more resident).
    # Uncompressed on purpose: a compressed bundle is inflated into RAM at launch (~50 MB more resident).
    dotnet publish src/Klock.Screensaver -r $rid -p:PublishReadyToRun=true @common -o (Join-Path $out 'scr')
    if ($LASTEXITCODE -ne 0) { throw "publish failed ($rid screensaver)" }

    Step "Publishing settings app ($rid)"
    dotnet publish src/Klock.Settings -r $rid -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true @common -o (Join-Path $out 'settings')
    if ($LASTEXITCODE -ne 0) { throw "publish failed ($rid settings)" }

    # A .scr is just a renamed exe: Windows launches it with /s /c /p.
    $stage = Join-Path $out 'stage'
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item (Join-Path $out 'scr\Klock.exe') (Join-Path $stage 'Klock.scr') -Force
    Copy-Item (Join-Path $out 'settings\Klock.Settings.exe') (Join-Path $stage 'Klock.Settings.exe') -Force
    Copy-Item (Join-Path $root 'assets\klock.ico') $stage -Force

    Step "Signing ($a)"
    Invoke-Sign @((Join-Path $stage 'Klock.scr'), (Join-Path $stage 'Klock.Settings.exe'))

    if (-not $SkipInstaller) {
        Step "Building installer ($a)"
        $iscc = $env:INNO_SETUP_PATH
        if (-not $iscc -or -not (Test-Path $iscc)) {
            $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
                Where-Object { Test-Path $_ } | Select-Object -First 1
        }
        if (-not $iscc) {
            Write-Warning 'Inno Setup 6 not found: skipping installer. Install it (winget install JRSoftware.InnoSetup) or set INNO_SETUP_PATH.'
        }
        else {
            & $iscc "/DAppVersion=$Version" "/DArch=$a" "/DStageDir=$stage" "/DOutDir=$dist" (Join-Path $root 'installer\klock.iss')
            if ($LASTEXITCODE -ne 0) { throw "installer build failed ($a)" }
            Invoke-Sign @((Join-Path $dist "Klock-Setup-$Version-$a.exe"))
        }
    }
}

Step 'Update manifest'
# Attach this file to the GitHub release; the app reads it from releases/latest/download/latest.json.
$repo = (Select-String -Path (Join-Path $root 'src\Klock.Core\AppInfo.cs') -Pattern 'GitHubRepo = "([^"]+)"').Matches[0].Groups[1].Value
[ordered]@{ version = $Version; url = "https://github.com/thewholesihle/releases/tag/v$Version"; notes = "Klock $Version" } |
    ConvertTo-Json | Set-Content (Join-Path $dist 'latest.json') -Encoding utf8

Step 'Done'
Get-ChildItem $dist -Recurse -Include Klock.scr, Klock.Settings.exe, Klock-Setup-*.exe |
    ForEach-Object { '{0,-70} {1,8:N1} MB' -f $_.FullName.Replace("$root\", ''), ($_.Length / 1MB) }


