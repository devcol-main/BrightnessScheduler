<#
.SYNOPSIS
  Builds release binaries into .\dist

  - BrightnessScheduler-<ver>-win-x64.exe        : self-contained single file (no .NET install needed)
  - BrightnessScheduler-<ver>-win-x64-small.exe  : framework-dependent (needs .NET 10 Desktop Runtime)
#>
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "src\BrightnessScheduler\BrightnessScheduler.csproj"
if (-not $Version) { $Version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Select-Object -First 1 }
$dist = Join-Path $root "dist"
$tmp  = Join-Path $root "dist\_tmp"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $tmp | Out-Null

Write-Host "Publishing self-contained v$Version ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true -o "$tmp\sc" `
  -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE) { throw "publish failed" }
Copy-Item "$tmp\sc\BrightnessScheduler.exe" "$dist\BrightnessScheduler-$Version-win-x64.exe"

Write-Host "Publishing framework-dependent v$Version ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained false -o "$tmp\fd" `
  -p:Version=$Version -p:PublishSingleFile=true -p:DebugType=none
if ($LASTEXITCODE) { throw "publish failed" }
Copy-Item "$tmp\fd\BrightnessScheduler.exe" "$dist\BrightnessScheduler-$Version-win-x64-small.exe"

Remove-Item $tmp -Recurse -Force
Get-ChildItem $dist | ForEach-Object { "{0,-50} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
