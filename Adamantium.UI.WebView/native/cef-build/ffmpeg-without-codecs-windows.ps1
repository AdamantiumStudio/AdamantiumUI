<#
.SYNOPSIS
  Builds the FFmpeg without H.264 and AAC (Chromium's branding) for a Windows build of build-windows.ps1.
.NOTES
  Only the ffmpeg library, in an out folder of its own with the main build's arguments but Chromium's FFmpeg: it
  exports what libcef takes from the one with codecs and stands in for it as a file.
  Usage:  powershell -File ffmpeg-without-codecs-windows.ps1 -Root D:\cef [-Jobs 44]
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [int]$Jobs = 16)
$ErrorActionPreference = 'Continue'
$env:PATH = "$Root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = '0'
$env:DEPOT_TOOLS_UPDATE = '0'
if (-not $env:vs2026_install) {
    $env:vs2026_install = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\18\BuildTools"
}
$src = "$Root\code\chromium\src"
$out = "$src\out\Release_GN_x64_FreeCodecs"
New-Item -ItemType Directory -Force $out | Out-Null
$lines = @(Get-Content "$src\out\Release_GN_x64\args.gn" | Where-Object { $_ -notmatch '^(ffmpeg_branding|proprietary_codecs)' })
$lines += 'ffmpeg_branding="Chromium"'
$lines += 'proprietary_codecs=false'
Set-Content -Path "$out\args.gn" -Value $lines -Encoding ascii
Set-Location $src
gn gen $out
if ($LASTEXITCODE -ne 0) { throw "gn gen failed: $LASTEXITCODE" }
autoninja -j $Jobs -C $out third_party/ffmpeg
if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }
Get-Item "$out\ffmpeg.dll" | Select-Object Name, Length, LastWriteTime
