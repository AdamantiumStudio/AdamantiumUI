<#
.SYNOPSIS
  Downloads the CEF build the bindings are generated for and stages its runtime under runtimes\<rid>\native\cef.
.NOTES
  Windows and Linux: our build (native/cef-build) from AdamantiumUI's releases, checked against the SHA-256 written
  here - H.264 and AAC behind the ProprietaryCodecs feature, FFmpeg without them. -Codecs puts an FFmpeg with them
  (ffmpeg.dll or libffmpeg.so of our build) in its place; WebView then turns the feature on. macOS: CEF's own build
  from cef-builds.spotifycdn.com, checked against its SHA-1. CEF goes to a folder of its own because it carries a
  vulkan-1.dll of its own, which next to the application would stand in for the system's loader. Its licence and
  Chromium's credits are staged beside it.
  Usage:  powershell -File native\fetch-cef.ps1 [-Rid win-x64|linux-x64|osx-arm64] [-Codecs <FFmpeg with codecs>]
#>
[CmdletBinding()]
param(
    [string]$Rid = "win-x64",
    [string]$Codecs)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$version = "156.0.26+g8ea0c73+chromium-156.0.8078.88"
$release = "cef-156.0.26-1"
$releases = "https://github.com/AdamantiumStudio/AdamantiumUI/releases/download/$release"
$ours = @{
    "win-x64" = @{ Platform = "windows64"; Sha256 = "c044612f98253f9fc072c281e4495910b5ecc8b7ecffea1f5748c60dcc93c15e"; FFmpeg = "ffmpeg.dll" }
    "linux-x64" = @{ Platform = "linux64"; Sha256 = "2e366cf6b5c75c83cab40398c0c69fd83494222452364926067b0eaf4a453b42"; FFmpeg = "libffmpeg.so" }
}
$build = $ours[$Rid]
if ($build) {
    $name = "$release-$($build.Platform)"
    $url = "$releases/$name.tar.bz2"
}
elseif ($Rid -eq "osx-arm64") {
    $name = "cef_binary_${version}_macosarm64_minimal"
    $url = "https://cef-builds.spotifycdn.com/" + [Uri]::EscapeDataString("$name.tar.bz2")
}
else {
    throw "Unknown runtime identifier '$Rid'."
}

if ($Codecs -and -not $build) { throw "-Codecs is for our builds - Windows and Linux." }
if ($Codecs -and -not (Test-Path $Codecs -PathType Leaf)) { throw "No FFmpeg at $Codecs." }

$project = Split-Path $PSScriptRoot -Parent
$work = Join-Path $PSScriptRoot "download"
$archive = Join-Path $work "$name.tar.bz2"
New-Item -ItemType Directory -Force $work | Out-Null

if (-not (Test-Path $archive)) {
    Write-Host "Downloading $name"
    Invoke-WebRequest $url -OutFile "$archive.part" -UseBasicParsing
    Move-Item "$archive.part" $archive
}

if ($build) {
    $expected = $build.Sha256
    $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $source = "this script says"
}
else {
    $sha1File = "$archive.sha1"
    if (-not (Test-Path $sha1File)) {
        Invoke-WebRequest "$url.sha1" -OutFile "$sha1File.part" -UseBasicParsing
        Move-Item "$sha1File.part" $sha1File
    }
    $expected = (Get-Content $sha1File -Raw).Trim()
    $actual = (Get-FileHash $archive -Algorithm SHA1).Hash.ToLowerInvariant()
    $source = "the CDN says"
}
if ($actual -ne $expected) {
    Remove-Item "$archive*"
    throw "The checksum of $name is $actual, $source $expected - removed it, run again."
}

# Unpacked aside and renamed when complete, so an interrupted run is never taken for a finished one.
$extract = Join-Path $work $name
if (-not (Test-Path $extract)) {
    $partial = Join-Path $work "unpacking"
    if (Test-Path $partial) { Remove-Item $partial -Recurse -Force }
    New-Item -ItemType Directory -Force $partial | Out-Null
    tar -xjf $archive -C $partial
    if ($LASTEXITCODE) { throw "tar failed" }
    Move-Item (Join-Path $partial $name) $extract
    Remove-Item $partial -Recurse -Force
}

$stage = Join-Path $project "runtimes\$Rid\native\cef"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item "$extract\Release\*" $stage -Recurse -Exclude "*.lib", "bootstrap*.exe"
if (Test-Path "$extract\Resources") { Copy-Item "$extract\Resources\*" $stage -Recurse }
Copy-Item "$extract\LICENSE.txt" $stage
if (Test-Path "$extract\CREDITS.html") { Copy-Item "$extract\CREDITS.html" $stage }
if ((Test-Path "$extract\FFMPEG.txt") -and -not $Codecs) { Copy-Item "$extract\FFMPEG.txt" $stage }
if ($Codecs) {
    Copy-Item $Codecs (Join-Path $stage $build.FFmpeg)
    Write-Host "FFmpeg with H.264 and AAC from $Codecs"
}
Write-Host "OK: CEF $version -> $stage" -ForegroundColor Green
