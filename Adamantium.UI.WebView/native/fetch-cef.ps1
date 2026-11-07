<#
.SYNOPSIS
  Downloads the CEF build the bindings are generated for and stages its runtime under runtimes\<rid>\native\cef.
.NOTES
  The minimal distribution from cef-builds.spotifycdn.com, checked against its SHA-1. CEF goes to a folder of its own
  because it carries a vulkan-1.dll of its own, which next to the application would stand in for the system's loader.
  Its licence and Chromium's credits are staged beside it.
  Usage:  powershell -File native\fetch-cef.ps1 [-Rid win-x64|linux-x64|osx-arm64]
#>
[CmdletBinding()]
param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$version = "156.0.26+g8ea0c73+chromium-156.0.8078.88"
$platform = @{ "win-x64" = "windows64"; "linux-x64" = "linux64"; "osx-arm64" = "macosarm64" }[$Rid]
if (-not $platform) { throw "Unknown runtime identifier '$Rid'." }

$project = Split-Path $PSScriptRoot -Parent
$name = "cef_binary_${version}_${platform}_minimal"
$url = "https://cef-builds.spotifycdn.com/" + [Uri]::EscapeDataString("$name.tar.bz2")
$work = Join-Path $PSScriptRoot "download"
$archive = Join-Path $work "$name.tar.bz2"
New-Item -ItemType Directory -Force $work | Out-Null

if (-not (Test-Path $archive)) {
    Write-Host "Downloading $name"
    Invoke-WebRequest $url -OutFile "$archive.part" -UseBasicParsing
    Move-Item "$archive.part" $archive
}

$sha1File = "$archive.sha1"
if (-not (Test-Path $sha1File)) {
    Invoke-WebRequest "$url.sha1" -OutFile "$sha1File.part" -UseBasicParsing
    Move-Item "$sha1File.part" $sha1File
}
$expected = (Get-Content $sha1File -Raw).Trim()
$actual = (Get-FileHash $archive -Algorithm SHA1).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    Remove-Item $archive, $sha1File
    throw "SHA-1 of $name is $actual, the CDN says $expected - removed it, run again."
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
Write-Host "OK: CEF $version -> $stage" -ForegroundColor Green
