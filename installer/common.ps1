# Ortak surum yardimcilari (Faz 2 / Task 4-5 review).
#
# publish.ps1 ve pack.ps1 surumu AYNI degerden, AYNI sekilde okumali (spec 8.9:
# surumun tek kaynagi Directory.Build.props). Ayni kod iki dosyada durunca biri
# duzeltilip digeri unutuluyor, o yuzden tek yerde.

function Get-BuildVersion {
    param([Parameter(Mandatory)][string]$PropsPath)

    if (-not (Test-Path $PropsPath)) { throw "Directory.Build.props bulunamadi: $PropsPath" }

    # SelectSingleNode: ".Project.PropertyGroup.Version" TEK bir <PropertyGroup> varsayar.
    # Ikinci bir PropertyGroup eklendigi an PowerShell bir DIZI dondurur ve surum
    # sessizce "2.2.0 " gibi bozuk bir metne donusur — paket yanlis damgalanir.
    $node = ([xml](Get-Content $PropsPath -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $node) { throw "Directory.Build.props icinde <Version> dugumu yok." }

    $version = $node.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+') {
        throw "Surum bicimi gecersiz: '$version' (beklenen 1.2.3 ya da 1.2.3-beta.1)."
    }
    return $version
}

function ConvertTo-FourPartVersion {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    # Exe damgasi (FileVersion) HER ZAMAN dort parcaya normalize edilir ve on-surum
    # ekini ("-beta.1") tumden kaybeder. Onek karsilastirmasi bu yuzden yanlis:
    # "2.2.0" ile "2.2.0.0" esit sayilmali, ama "2.2.0" ile "2.2.01" esit SAYILMAMALI.
    $core = ($Value -split '-')[0]
    $parsed = $null
    if (-not [version]::TryParse($core, [ref]$parsed)) { return $null }
    return [version]::new($parsed.Major, $parsed.Minor,
                          [Math]::Max($parsed.Build, 0), [Math]::Max($parsed.Revision, 0))
}

function Get-VelopackPackageVersion {
    param([Parameter(Mandatory)][string]$ProjectPath)

    if (-not (Test-Path $ProjectPath)) { throw "Proje bulunamadi: $ProjectPath" }

    $node = ([xml](Get-Content $ProjectPath -Raw)).SelectSingleNode(
        "/Project/ItemGroup/PackageReference[@Include='Velopack']")
    if ($null -eq $node) { throw "$ProjectPath icinde Velopack PackageReference yok." }

    $version = $node.GetAttribute('Version')
    if ([string]::IsNullOrWhiteSpace($version)) { throw "Velopack PackageReference'inda Version yok." }
    return $version.Trim()
}

function Get-InstalledVpkVersion {
    # 'dotnet tool list --global' sutunlari: Package Id / Version / Commands.
    $line = dotnet tool list --global | Select-String -Pattern '^\s*vpk\s+(\S+)'
    if (-not $line) { return $null }
    return $line.Matches[0].Groups[1].Value
}
