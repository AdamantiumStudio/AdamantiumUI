<#
.SYNOPSIS
  Builds our CEF for Windows x64 from a checkout CEF's automate-git.py fetched, and its minimal distribution.
.NOTES
  The steps automate-git takes, by hand to bound the jobs: CEF's patches and GN files (gclient_hook), our patches,
  the build, make_distrib. Official build with PGO, proprietary codecs behind the ProprietaryCodecs feature, FFmpeg as
  a library of its own (ffmpeg.dll, with Chrome's codecs), no OpenH264. The FFmpeg without codecs comes after it from
  ffmpeg-without-codecs-windows.ps1, the release archive from pack.sh. See README.md.
  Usage:  powershell -File build-windows.ps1 -Root D:\cef [-Jobs 44] [-Resume]
  -Resume continues a build that stopped: the patches and the GN files it has are kept.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [int]$Jobs = 16,
    [switch]$Resume)
# Not 'Stop': Windows PowerShell takes any stderr line of a native tool (autoninja prints a hint there) for an error.
# Each step's exit code decides instead.
$ErrorActionPreference = 'Continue'

$env:PATH = "$Root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = '0'
$env:DEPOT_TOOLS_UPDATE = '0'
if (-not $env:vs2026_install) {
    $env:vs2026_install = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\18\BuildTools"
}
# No OpenH264: proprietary_codecs would link its software H.264 encoder into libcef itself.
$env:GN_DEFINES = 'is_official_build=true proprietary_codecs=true ffmpeg_branding=Chrome is_component_ffmpeg=true media_use_openh264=false'
$env:CEF_ARCHIVE_FORMAT = 'tar.bz2'
# Only the configuration built: the x86 ones want PGO profiles of their own.
$env:GN_OUT_CONFIGS = 'Release_GN_x64'
$src = "$Root\code\chromium\src"
$patches = Join-Path $PSScriptRoot 'patches'

function Invoke-PatchOnce([string]$Repository, [string]$Patch) {
    git -C $Repository apply --reverse --check $Patch 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Output "applied already: $(Split-Path $Patch -Leaf)"
        return
    }

    git -C $Repository apply $Patch
    if ($LASTEXITCODE -ne 0) { throw "$Patch does not apply to $Repository" }
    Write-Output "applied: $(Split-Path $Patch -Leaf)"
}

Write-Output "== start $(Get-Date -Format 'dd.MM HH:mm')"
if (-not $Resume) {
    Set-Location "$src\cef"
    python3 tools\gclient_hook.py
    if ($LASTEXITCODE -ne 0) { throw "gclient_hook failed: $LASTEXITCODE" }
}

Invoke-PatchOnce $src "$patches\chromium-proprietary-codecs-gate.patch"
Invoke-PatchOnce "$src\cef" "$patches\cef-make-distrib-ffmpeg.patch"
Write-Output "== args"
Get-Content "$src\out\Release_GN_x64\args.gn"

Write-Output "== build $(Get-Date -Format 'dd.MM HH:mm') with $Jobs jobs"
Set-Location $src
autoninja -j $Jobs -C out\Release_GN_x64 cefsimple bootstrap bootstrapc
if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }

Write-Output "== distrib $(Get-Date -Format 'dd.MM HH:mm')"
Set-Location "$src\cef\tools"
python3 make_distrib.py --output-dir="$src\cef\binary_distrib" --ninja-build --x64-build --minimal --allow-partial
if ($LASTEXITCODE -ne 0) { throw "distrib failed: $LASTEXITCODE" }
Write-Output "== done $(Get-Date -Format 'dd.MM HH:mm')"
Get-ChildItem "$src\cef\binary_distrib" | Select-Object Name, Length
