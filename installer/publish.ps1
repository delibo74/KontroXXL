# KontroXXL yayin profili (Faz 2 / Task 4).
#
# framework-dependent win-x64 yayin uretir; ciktisi installer/pack.ps1'in
# (Velopack) girdisidir. Makinede yalnizca .NET SDK 8 var, self-contained
# yayin ~70 MB'lik bir paket demek olurdu — spec 5 framework-dependent diyor.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root  = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj  = Join-Path $root "src\KontroXXL_WinApp\KontroXXL_WinApp.csproj"
$out   = Join-Path $root "publish"
$props = Join-Path $root "Directory.Build.props"

if (-not (Test-Path $proj))  { throw "Proje bulunamadi: $proj" }
if (-not (Test-Path $props)) { throw "Directory.Build.props bulunamadi: $props" }

# Spec 8.9 — surumun tek kaynagi Directory.Build.props.
$version = ([xml](Get-Content $props -Raw)).Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "Directory.Build.props icinde <Version> okunamadi." }
Write-Host "Surum: $version"

# Eski cikti tamamen silinir: artik uretilmeyen bir dosyanin pakete sizmasi
# (ornegin kaldirilmis bir bagimlilik DLL'i) sessiz bir hata kaynagidir.
# Guvenlik: yalnizca depo altindaki 'publish' klasoru silinir.
if (Test-Path $out) {
    $resolved = (Resolve-Path $out).Path
    if ($resolved -ne (Join-Path $root "publish")) { throw "Beklenmeyen cikti yolu: $resolved" }
    Remove-Item $resolved -Recurse -Force
}

dotnet publish $proj -c Release -r win-x64 --self-contained false -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish basarisiz (cikis kodu $LASTEXITCODE)." }

$exe = Join-Path $out "KontroXXL_WinApp.exe"
if (-not (Test-Path $exe)) { throw "Yayin uretildi ama $exe yok." }

# Spec 8.9 kapisi: exe'nin damgasi Directory.Build.props ile ayni olmali.
# Ayarlar'daki "Hakkinda" satiri da ayni damgayi okuyor, boylece uc yer tek degerde bulusuyor.
$stamped = (Get-Item $exe).VersionInfo.FileVersion
if (-not $stamped.StartsWith($version)) {
    throw "Surum uyusmazligi: Directory.Build.props '$version' diyor, exe '$stamped' damgali."
}

Write-Host "Yayin hazir: $out"
Write-Host "  $([System.IO.Path]::GetFileName($exe)) -> $stamped"
