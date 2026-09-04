# KontroXXL kurulum paketi (Faz 2 / Task 5) — Velopack.
#
# Once publish.ps1 calisir (yayin + surum kapisi), sonra vpk o klasoru paketler.
# Cikti: releases\KontroXXL-win-Setup.exe
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common.ps1")

$root      = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishSc = Join-Path $PSScriptRoot "publish.ps1"
$publishOut= Join-Path $root "publish"
$releases  = Join-Path $root "releases"
$props     = Join-Path $root "Directory.Build.props"
$icon      = Join-Path $root "icon.ico"
$proj      = Join-Path $root "src\KontroXXL_WinApp\KontroXXL_WinApp.csproj"

# vpk SURUMU SABIT: 'dotnet tool install -g vpk' sabitlenmeden calistirilirsa temiz bir
# makineye o gunun en yeni vpk'si iner ve projedeki Velopack kutuphanesiyle uyusmayan bir
# paket uretir (paket bicimi/hook sozlesmesi surumler arasi degisiyor). Kaynak: csproj.
$velopack = Get-VelopackPackageVersion -ProjectPath $proj

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk bulunamadi. Once: dotnet tool install -g vpk --version $velopack"
}

# publish.ps1'deki damga kapisinin esdegeri: kurulu arac ile kutuphane ayni surumde mi?
$vpkVersion = Get-InstalledVpkVersion
if ([string]::IsNullOrWhiteSpace($vpkVersion)) {
    throw "vpk kurulu gorunuyor ama surumu okunamadi ('dotnet tool list --global')."
}
if ($vpkVersion -ne $velopack) {
    throw ("vpk surumu ($vpkVersion), Velopack PackageReference ($velopack) ile uyusmuyor. " +
           "Duzelt: dotnet tool update -g vpk --version $velopack")
}

& $publishSc
if ($LASTEXITCODE -ne 0) { throw "publish.ps1 basarisiz (cikis kodu $LASTEXITCODE)." }

# Spec 8.9 — surum publish.ps1 ile AYNI kaynaktan, AYNI kodla okunur; paket damgasi da bu olur.
$version = Get-BuildVersion -PropsPath $props

if (-not (Test-Path $icon)) { throw "Ikon bulunamadi: $icon" }
if (-not (Test-Path (Join-Path $publishOut "KontroXXL_WinApp.exe"))) { throw "Yayin ciktisi eksik: $publishOut" }

# ON-TEMIZLIK — vpk pack IDEMPOTENT DEGIL: outputDir'de paketlenmekte olan surumun
# nupkg'si zaten duruyorsa cikis kodu -1 ile duser ve SEBEBINI SOYLEMEZ ("vpk pack
# basarisiz (cikis kodu -1)"). 2026-09-04'te 2.2.1'i yeniden paketlerken tam bu tuzaga
# dusuldu. Bu yuzden YALNIZCA paketlenen surumun kendi ciktilarini siliyoruz.
# ONCEKI surumlerin *-full.nupkg dosyalarina DOKUNULMAZ: delta uretimi (2.2.0 -> 2.2.1)
# onlari okur, silinirlerse delta hic uretilmez ve guncelleme tam paket indirmeye duser.
$stale = @(
    (Join-Path $releases "KontroXXL-$version-full.nupkg"),
    (Join-Path $releases "KontroXXL-$version-delta.nupkg"),
    (Join-Path $releases "KontroXXL-win-Setup.exe"),
    (Join-Path $releases "KontroXXL-win-Portable.zip")
)
foreach ($f in $stale) {
    if (Test-Path $f) {
        Write-Host "On-temizlik: $([System.IO.Path]::GetFileName($f)) siliniyor (ayni surum yeniden paketleniyor)."
        Remove-Item $f -Force
    }
}

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
