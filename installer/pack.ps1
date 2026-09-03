# KontroXXL kurulum paketi (Faz 2 / Task 5) — Velopack.
#
# Once publish.ps1 calisir (yayin + surum kapisi), sonra vpk o klasoru paketler.
# Cikti: releases\KontroXXL-win-Setup.exe
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root      = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishSc = Join-Path $PSScriptRoot "publish.ps1"
$publishOut= Join-Path $root "publish"
$releases  = Join-Path $root "releases"
$props     = Join-Path $root "Directory.Build.props"
$icon      = Join-Path $root "icon.ico"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk bulunamadi. Once: dotnet tool install -g vpk"
}

& $publishSc
if ($LASTEXITCODE -ne 0) { throw "publish.ps1 basarisiz (cikis kodu $LASTEXITCODE)." }

# Spec 8.9 — surum publish.ps1 ile AYNI kaynaktan okunur; paket damgasi da bu olur.
$version = ([xml](Get-Content $props -Raw)).Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "Directory.Build.props icinde <Version> okunamadi." }

if (-not (Test-Path $icon)) { throw "Ikon bulunamadi: $icon" }
if (-not (Test-Path (Join-Path $publishOut "KontroXXL_WinApp.exe"))) { throw "Yayin ciktisi eksik: $publishOut" }

# --framework: yayin framework-dependent, temiz bir makinede .NET 8 Desktop runtime
# olmayabilir (spec 8.3). Bu bayrak olmadan kurulum sessizce acilmayan bir uygulama birakir;
# Velopack bootstrapper'i runtime'i once kurar.
vpk pack `
    --packId KontroXXL `
    --packVersion $version `
    --packDir $publishOut `
    --mainExe KontroXXL_WinApp.exe `
    --packTitle "KontroXXL" `
    --packAuthors "KontroXXL" `
    --icon $icon `
    --framework net8.0-x64-desktop `
    --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack basarisiz (cikis kodu $LASTEXITCODE)." }

$setup = Join-Path $releases "KontroXXL-win-Setup.exe"
if (-not (Test-Path $setup)) { throw "Paketleme bitti ama $setup yok." }
Write-Host "Kurulum paketi hazir: $setup  (surum $version)"
