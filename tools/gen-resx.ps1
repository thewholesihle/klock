# Generates Strings*.resx from tools/strings/<culture>.txt (key=value per line, '#' comments, \n escapes).
# en.txt is the neutral resource; every other file becomes Strings.<culture>.resx.
param(
    [string]$SourceDir = (Join-Path $PSScriptRoot 'strings'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\Klock.Settings\Resources')
)

New-Item -ItemType Directory -Force $OutDir | Out-Null

$header = @'
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
'@

$en = @{}
foreach ($line in Get-Content (Join-Path $SourceDir 'en.txt') -Encoding UTF8) {
    if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
    $i = $line.IndexOf('='); $en[$line.Substring(0, $i).Trim()] = $line.Substring($i + 1)
}

foreach ($file in Get-ChildItem $SourceDir -Filter *.txt) {
    $culture = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine($header)
    $count = 0; $missing = @()
    $map = [ordered]@{}
    foreach ($line in Get-Content $file.FullName -Encoding UTF8) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $i = $line.IndexOf('=')
        $map[$line.Substring(0, $i).Trim()] = $line.Substring($i + 1).Replace('\n', "`n")
    }
    foreach ($k in $map.Keys) {
        $v = [Security.SecurityElement]::Escape($map[$k])
        [void]$sb.AppendLine("  <data name=`"$k`" xml:space=`"preserve`"><value>$v</value></data>")
        $count++
    }
    if ($culture -ne 'en') { $missing = $en.Keys | Where-Object { -not $map.Contains($_) } }
    [void]$sb.AppendLine('</root>')
    $name = if ($culture -eq 'en') { 'Strings.resx' } else { "Strings.$culture.resx" }
    [IO.File]::WriteAllText((Join-Path $OutDir $name), $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
    $msg = "$name : $count strings"
    if ($missing.Count -gt 0) { $msg += " (MISSING $($missing.Count): $($missing -join ', '))" }
    Write-Host $msg
}
