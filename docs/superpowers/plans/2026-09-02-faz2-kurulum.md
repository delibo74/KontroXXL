# KontroXXL Faz 2 — Kurulum ve Güvenlik Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** KontroXXL'i çift tıkla kurulan, kendini güncelleyen, sırrını şifreli tutan ve Arduino'yu da kendisi programlayan bir pakete dönüştürmek.

**Architecture:** Yazılabilir durum (`config.json`, loglar, `crash.log`) kurulum dizininden `%APPDATA%\KontroXXL\` altına taşınır — Velopack kurulum klasörünü her güncellemede değiştirdiği için bu bir ön koşuldur. API anahtarı DPAPI ile `CurrentUser` kapsamında şifrelenir. Paketleme ve delta güncelleme Velopack'e, firmware yükleme Arduino IDE'den kopyalanan avrdude'a devredilir.

**Tech Stack:** C# 12, .NET 8.0 (SDK 8.0.301), xUnit, Velopack, `System.Security.Cryptography.ProtectedData`, avrdude 8.0.0-arduino1, `gh` CLI

**Spec:** `docs/superpowers/specs/2026-09-02-kontroxxl-faz2.md`

## Global Constraints

- `KontroXXL.Core` hedefi **`net8.0`**; şu assembly'lere **asla** referans veremez: `System.Windows.Forms`, `System.IO.Ports`, `System.Management`, `Microsoft.Win32.Registry`, `AudioSwitcher.*`, `Avalonia`. `ArchitectureTests` bunu **önek eşleştirmeyle** zorluyor — Faz 1'de düzeltildi, bozma.
- `KontroXXL.Core` ayarları: `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Herhangi bir uyarı derlemeyi kırar.
- `KontroXXL_WinApp` hedefi **`net8.0-windows`**, `<Nullable>disable</Nullable>`. `<NoWarn>` listesi `NU1701;MSB3245;MSB3243` — `CS0169` Faz 1'de kaldırıldı, geri ekleme.
- Makinede yalnızca SDK **8.0.301** kurulu. Hiçbir `TargetFramework` `net8.0`'ın üstüne çıkmaz.
- LCD'ye giden her satır tam **16 karakter**; yalnızca ASCII 0x20–0x7E + `\x01`/`\x02`. Seri protokol donmuş: `L0=`, `L1=`, `B1=`, `CLR`, `ON`, `OFF` / `EV:UP`, `EV:DN`, `EV:CLICK`, `EV:BACK`, `CMD:READY`, `CMD:UPDATE`, `CMD:APPS`, `CMD:POOLS`, `CMD:SHORTCUTS`. **Arduino firmware'i bu fazda değiştirilmez.**
- Her `AppConfig` durum yazımı `lock (config.SyncRoot)` altında. LCD durumu yalnızca UI thread'inde (doğrudan veya `RunOnUi` ile).
- Arduino kartı **Uno**: avrdude `-c arduino -b 115200`.
- Kurulum **kullanıcı bazlı** (`%LOCALAPPDATA%`), makine geneli değil. UAC istemi çıkmaz.
- Kod tanımlayıcıları İngilizce; yorum, log ve doküman metni Türkçe.
- Her commit öncesi `dotnet build KontroXXL.sln -c Release` ve `dotnet test` yeşil.
- Çalışma dizini: `C:\Users\ibrahimk\Desktop\nas-lcd`, dal `faz2-kurulum` (Task 1'de `v2.1.0`'dan açılır).

## Kullanıcıya borçlu ön koşullar

Bu iki madde olmadan Task 6 ve Task 7 tamamlanamaz:

1. **Firmware `.hex`:** Arduino IDE → `firmware/arduino_kontrol/arduino_kontrol.ino` aç → Sketch → Export Compiled Binary → çıkan `.hex` dosyası `firmware/arduino_kontrol.ino.hex` olarak depoya konur.
2. **GitHub deposu:** adı ve görünürlüğü (public/private) Karaduman'ın kararı; depo oluşturma ve ilk push ayrı ayrı onaylanır (spec §7).

## Detay seviyesi hakkında dürüst not

Task 1–3 saf, test edilebilir işler ve tam TDD döngüsüyle yazıldı. Task 4–8 paketleme
ve entegrasyon işleri — birim testle kapsanamazlar (dosya sistemi, harici process,
kurulum davranışı). Onlar için doğrulama, spec §8'deki kabul kriterleri ve
her görevin sonundaki somut elle kontrol listesidir. Sahte test yazılmaz.

---

## File Structure

**Oluşturulacak:**

| Dosya | Sorumluluk |
|---|---|
| `src/KontroXXL.Core/Configuration/AppPaths.cs` | Yazılabilir durumun yolları, tek kaynak |
| `src/KontroXXL.Core/Configuration/ConfigMigrator.cs` | Eski konumdan tek seferlik, idempotent göç |
| `src/KontroXXL.Core/Security/ISecretProtector.cs` | Sır şifreleme arayüzü + test implementasyonu |
| `src/KontroXXL_WinApp/DpapiSecretProtector.cs` | DPAPI `CurrentUser` implementasyonu |
| `src/KontroXXL_WinApp/FirmwareFlasher.cs` | avrdude sarmalayıcı + port doğrulama |
| `installer/pack.ps1` | `vpk pack` betiği |
| `installer/tools/avrdude/` | Arduino IDE'den kopyalanan avrdude |
| `firmware/arduino_kontrol.ino.hex` | Kullanıcının export ettiği firmware |
| `tests/KontroXXL.Core.Tests/Configuration/AppPathsTests.cs` | |
| `tests/KontroXXL.Core.Tests/Configuration/ConfigMigratorTests.cs` | |

**Değiştirilecek:** `Models.cs`, `Program.cs`, `TrayApplicationContext.cs`, `MainForm.cs`, `RollingFileLogger.cs`, `JsonFileStore.cs`, `KontroXXL_WinApp.csproj`, `Directory.Build.props`, dokümanlar.

---

## Task 1: `AppPaths` + `%APPDATA%` göçü (A6)

**Files:**
- Create: `src/KontroXXL.Core/Configuration/AppPaths.cs`, `src/KontroXXL.Core/Configuration/ConfigMigrator.cs`
- Test: `tests/KontroXXL.Core.Tests/Configuration/AppPathsTests.cs`, `.../ConfigMigratorTests.cs`
- Modify: `src/KontroXXL_WinApp/Models.cs`, `src/KontroXXL_WinApp/Program.cs`, `src/KontroXXL_WinApp/TrayApplicationContext.cs`

**Interfaces:**
- Consumes: `JsonFileStore` (Faz 1)
- Produces:
  - `sealed class AppPaths` — ctor `(string root)`; `Root`, `ConfigFile`, `LogDir`, `LogFile`, `CrashLog`; `static AppPaths ForCurrentUser()`
  - `static class ConfigMigrator` — `static bool MigrateIfNeeded(string legacyFile, string targetFile)`
  - `AppConfig.SchemaVersion` (int, varsayılan 3)

- [x] **Step 1: Dalı aç**

```bash
cd "C:/Users/ibrahimk/Desktop/nas-lcd"
git switch -c faz2-kurulum v2.1.0
```

- [x] **Step 2: Başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Configuration/AppPathsTests.cs`:
```csharp
using KontroXXL.Core.Configuration;
using Xunit;

namespace KontroXXL.Core.Tests.Configuration;

public class AppPathsTests
{
    [Fact]
    public void All_paths_live_under_the_given_root()
    {
        var p = new AppPaths(@"X:\kok");
        Assert.Equal(@"X:\kok", p.Root);
        Assert.StartsWith(@"X:\kok", p.ConfigFile);
        Assert.StartsWith(@"X:\kok", p.LogFile);
        Assert.StartsWith(@"X:\kok", p.CrashLog);
    }

    [Fact]
    public void Config_and_logs_have_the_expected_names()
    {
        var p = new AppPaths(@"X:\kok");
        Assert.Equal(@"X:\kok\config.json", p.ConfigFile);
        Assert.Equal(@"X:\kok\logs", p.LogDir);
        Assert.Equal(@"X:\kok\logs\app.log", p.LogFile);
        Assert.Equal(@"X:\kok\crash.log", p.CrashLog);
    }

    [Fact]
    public void ForCurrentUser_points_at_roaming_appdata_not_the_install_dir()
    {
        var p = AppPaths.ForCurrentUser();
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith(roaming, p.Root);
        Assert.EndsWith("KontroXXL", p.Root);

        // Velopack kurulum kokunu %LOCALAPPDATA%\KontroXXL yapiyor; carpismamali.
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.False(p.Root.StartsWith(local, StringComparison.OrdinalIgnoreCase),
            $"config koku Velopack kurulum koku ile ayni dalda: {p.Root}");
    }
}
```

`tests/KontroXXL.Core.Tests/Configuration/ConfigMigratorTests.cs`:
```csharp
using KontroXXL.Core.Configuration;
using Xunit;

namespace KontroXXL.Core.Tests.Configuration;

public class ConfigMigratorTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "kx-mig-" + Guid.NewGuid().ToString("N"));

    public ConfigMigratorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string Legacy => Path.Combine(_dir, "eski", "config.json");
    string Target => Path.Combine(_dir, "yeni", "config.json");

    void WriteLegacy(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Legacy)!);
        File.WriteAllText(Legacy, content);
    }

    [Fact]
    public void Copies_the_legacy_file_when_the_target_is_absent()
    {
        WriteLegacy("{\"TruenasIp\":\"192.168.50.163\"}");

        Assert.True(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.Equal("{\"TruenasIp\":\"192.168.50.163\"}", File.ReadAllText(Target));
    }

    [Fact]
    public void Creates_the_target_directory()
    {
        WriteLegacy("{}");
        ConfigMigrator.MigrateIfNeeded(Legacy, Target);
        Assert.True(File.Exists(Target));
    }

    [Fact]
    public void Never_overwrites_an_existing_target()
    {
        WriteLegacy("ESKI");
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        File.WriteAllText(Target, "YENI");

        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.Equal("YENI", File.ReadAllText(Target));
    }

    [Fact]
    public void Does_nothing_when_there_is_no_legacy_file()
        => Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));

    [Fact]
    public void Is_idempotent_across_repeated_startups()
    {
        WriteLegacy("{}");
        Assert.True(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
    }

    [Fact]
    public void Leaves_the_legacy_file_in_place_as_a_fallback()
    {
        WriteLegacy("{}");
        ConfigMigrator.MigrateIfNeeded(Legacy, Target);
        Assert.True(File.Exists(Legacy), "eski dosya silinmemeli — geri donus yolu");
    }

    [Fact]
    public void Returns_false_instead_of_throwing_when_the_legacy_file_is_unreadable()
    {
        WriteLegacy("{}");
        using var hold = new FileStream(Legacy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.False(ConfigMigrator.MigrateIfNeeded(Legacy, Target));
        Assert.False(File.Exists(Target));
    }
}
```

- [x] **Step 3: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter "AppPathsTests|ConfigMigratorTests"`
Expected: FAIL — `AppPaths` ve `ConfigMigrator` bulunamıyor (CS0246).

- [x] **Step 4: İki tipi yaz**

`src/KontroXXL.Core/Configuration/AppPaths.cs`:
```csharp
namespace KontroXXL.Core.Configuration;

/// <summary>
/// Yazilabilir her seyin yolu buradan gecer.
/// A6: v2 bunlari exe'nin yanina yaziyordu. Velopack kurulum klasorunu (`current/`)
/// her guncellemede DEGISTIRDIGI icin oraya yazilan her sey guncellemede kaybolur;
/// Program Files'a kurulunca da yazma izni yoktur.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "KontroXXL";

    public string Root { get; }

    public AppPaths(string root) => Root = root;

    public string ConfigFile => Path.Combine(Root, "config.json");
    public string LogDir => Path.Combine(Root, "logs");
    public string LogFile => Path.Combine(LogDir, "app.log");
    public string CrashLog => Path.Combine(Root, "crash.log");

    /// <summary>
    /// Roaming AppData altinda. Velopack kurulumu %LOCALAPPDATA%\KontroXXL kullaniyor —
    /// kasten farkli dal, kaldirma sirasinda kullanici verisi silinmesin diye.
    /// </summary>
    public static AppPaths ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));
}
```

`src/KontroXXL.Core/Configuration/ConfigMigrator.cs`:
```csharp
namespace KontroXXL.Core.Configuration;

/// <summary>
/// v2 yapilandirmasini exe'nin yanindan %APPDATA%'ya tasir. Her acilista calisir
/// ve idempotenttir: hedef zaten varsa hicbir sey yapmaz, boylece kullanicinin
/// yeni ayarlari eski dosyayla ezilmez.
/// </summary>
public static class ConfigMigrator
{
    /// <returns>Goc GERCEKTEN yapildiysa true.</returns>
    public static bool MigrateIfNeeded(string legacyFile, string targetFile)
    {
        if (File.Exists(targetFile)) return false;
        if (!File.Exists(legacyFile)) return false;

        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(targetFile)) ?? ".";
            Directory.CreateDirectory(dir);

            // Kopyala, TASIMA — eski dosya geri donus yolu olarak kalir.
            File.Copy(legacyFile, targetFile, overwrite: false);
            return true;
        }
        catch
        {
            // Goc edilemezse uygulama varsayilanlarla acilir; cokmez.
            return false;
        }
    }
}
```

- [x] **Step 5: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter "AppPathsTests|ConfigMigratorTests"`
Expected: PASS — 10 test.

- [x] **Step 6: `AppConfig`'e `SchemaVersion` ekle**

`src/KontroXXL_WinApp/Models.cs`, `ArduinoPort` alanının hemen üstüne:
```csharp
        // Faz 2: yapilandirma semasi surumu. 3 = %APPDATA% donemi.
        public int SchemaVersion { get; set; } = 3;
```

- [x] **Step 7: Uygulamayı yeni yollara bağla**

`src/KontroXXL_WinApp/TrayApplicationContext.cs` — sınıfa alan ekle ve ctor'un en başında göçü çalıştır. `config = AppConfig.Load();` satırını bul ve bloğu şununla değiştir:

```csharp
                // A6: yazilabilir durum artik %APPDATA%\KontroXXL altinda.
                paths = AppPaths.ForCurrentUser();
                Directory.CreateDirectory(paths.Root);

                string legacyConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                bool migrated = ConfigMigrator.MigrateIfNeeded(legacyConfig, paths.ConfigFile);

                config = AppConfig.Load(paths.ConfigFile);
                if (migrated) { config.SchemaVersion = 3; config.MarkDirty(); }
```

Alan tanımı (`private AppConfig config;` yanına):
```csharp
        private AppPaths paths;
```

`using KontroXXL.Core.Configuration;` ekle.

Logger kurulumunu bul (`new RollingFileLogger(logFile, ...)`) ve yolu değiştir:
```csharp
                    log = new RollingFileLogger(paths.LogFile, LogLevel.Info);
```

`src/KontroXXL_WinApp/Program.cs` — üç `crash.log` yazımı da `BaseDirectory`'ye gidiyor. Hepsini `%APPDATA%`'ya taşı ve **üzerine yazmak yerine ekle** (D4: `crash.log` A1'in kanıtıydı, eski kayıtları ezmemeli). `Main`'in başına yardımcı ekle:

```csharp
        static void WriteCrash(string message)
        {
            try
            {
                var p = KontroXXL.Core.Configuration.AppPaths.ForCurrentUser();
                Directory.CreateDirectory(p.Root);
                File.AppendAllText(p.CrashLog,
                    $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }
```

Üç `File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), …)` çağrısını `WriteCrash(…)` ile değiştir.

- [x] **Step 8: Derle, test et, elle doğrula**

Run: `dotnet build KontroXXL.sln -c Release && dotnet test`
Expected: yeşil, 161 test (151 + 10).

Elle: `dotnet run --project src/KontroXXL_WinApp` (eski instance kapalıyken) → `%APPDATA%\KontroXXL\config.json` ve `%APPDATA%\KontroXXL\logs\app.log` oluşmalı; `bin/Release/.../config.json` **oluşmamalı**.

- [x] **Step 9: Commit**

```bash
git add src/KontroXXL.Core/Configuration tests/KontroXXL.Core.Tests/Configuration src/KontroXXL_WinApp/Models.cs src/KontroXXL_WinApp/Program.cs src/KontroXXL_WinApp/TrayApplicationContext.cs
git commit -m "feat(paths): move writable state to %APPDATA% with one-time migration (A6)"
```

---

## Task 2: DPAPI ile API anahtarı şifreleme (A5)

**Files:**
- Create: `src/KontroXXL.Core/Security/ISecretProtector.cs`, `src/KontroXXL_WinApp/DpapiSecretProtector.cs`
- Test: `tests/KontroXXL.Core.Tests/Security/SecretProtectorTests.cs`
- Modify: `src/KontroXXL_WinApp/Models.cs`, `src/KontroXXL_WinApp/TrayApplicationContext.cs`, `src/KontroXXL_WinApp/MainForm.cs`, `src/KontroXXL_WinApp/KontroXXL_WinApp.csproj`

**Interfaces:**
- Consumes: `AppPaths` (Task 1)
- Produces:
  - `interface ISecretProtector { string Protect(string? plain); string? Unprotect(string? cipher); }`
  - `sealed class PlaintextSecretProtector : ISecretProtector` (Core, testler ve Windows dışı için)
  - `sealed class DpapiSecretProtector : ISecretProtector` (WinApp)
  - `AppConfig.TruenasApiKeyProtected` (string, serileşir) ve `AppConfig.TruenasApiKey` (`[JsonIgnore]`, bellekte)
  - `AppConfig.ApplyProtection(ISecretProtector)` / `AppConfig.UnprotectSecrets(ISecretProtector)`

- [x] **Step 1: Başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Security/SecretProtectorTests.cs`:
```csharp
using KontroXXL.Core.Security;
using Xunit;

namespace KontroXXL.Core.Tests.Security;

public class PlaintextSecretProtectorTests
{
    readonly ISecretProtector _p = new PlaintextSecretProtector();

    [Fact]
    public void Round_trips_a_value()
        => Assert.Equal("7-3gnEG", _p.Unprotect(_p.Protect("7-3gnEG")));

    [Fact]
    public void Protecting_null_or_empty_yields_empty()
    {
        Assert.Equal("", _p.Protect(null));
        Assert.Equal("", _p.Protect(""));
    }

    [Fact]
    public void Unprotecting_null_or_empty_yields_null()
    {
        Assert.Null(_p.Unprotect(null));
        Assert.Null(_p.Unprotect(""));
    }

    [Fact]
    public void Unprotect_returns_null_rather_than_throwing_on_garbage()
        => Assert.Null(_p.Unprotect("bu-gecerli-bir-sifreli-metin-degil-!!!"));
}
```

- [x] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter PlaintextSecretProtectorTests`
Expected: FAIL — `ISecretProtector` bulunamıyor.

- [x] **Step 3: Core arayüzünü yaz**

`src/KontroXXL.Core/Security/ISecretProtector.cs`:
```csharp
using System.Text;

namespace KontroXXL.Core.Security;

/// <summary>
/// A5: TrueNAS API anahtari diske duz metin yazilmaz.
/// Gercek implementasyon Windows DPAPI kullanir; Core platform-bagimsiz kalmak
/// zorunda oldugu icin burada yalnizca sozlesme ve test implementasyonu durur.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Bos/null girdi icin bos dizge dondurur — asla null.</summary>
    string Protect(string? plain);

    /// <summary>Cozulemezse null dondurur, ISTISNA FIRLATMAZ.</summary>
    string? Unprotect(string? cipher);
}

/// <summary>Sifrelemeyen implementasyon: testler ve Windows disi ortamlar icin.</summary>
public sealed class PlaintextSecretProtector : ISecretProtector
{
    public string Protect(string? plain) =>
        string.IsNullOrEmpty(plain) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
        catch { return null; }
    }
}
```

- [x] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter PlaintextSecretProtectorTests`
Expected: PASS — 4 test.

- [x] **Step 5: DPAPI implementasyonunu yaz**

`src/KontroXXL_WinApp/KontroXXL_WinApp.csproj` `ItemGroup`'una ekle:
```xml
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
```

`src/KontroXXL_WinApp/DpapiSecretProtector.cs`:
```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using KontroXXL.Core.Security;

namespace KontroXXL_WinApp
{
    /// <summary>
    /// Windows DPAPI, CurrentUser kapsami. Sifreli metin yalnizca ayni Windows
    /// kullanici profilinde cozulebilir — dosya kopyalansa bile baska profilde ise yaramaz.
    /// </summary>
    public sealed class DpapiSecretProtector : ISecretProtector
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KontroXXL/v1");

        public string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] blob = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(blob);
            }
            catch { return ""; }
        }

        public string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return null;
            try
            {
                byte[] blob = ProtectedData.Unprotect(
                    Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(blob);
            }
            catch
            {
                // Profil/makine degisti ya da veri bozuk. Sessizce basarisiz OLMA —
                // cagiran taraf kullaniciya Ayarlar'da yeniden girmesini soyleyecek.
                return null;
            }
        }
    }
}
```

- [x] **Step 6: `AppConfig`'i şifreli alana geçir**

`src/KontroXXL_WinApp/Models.cs` — `TruenasApiKey` satırını şununla değiştir:
```csharp
        // A5: diske SIFRELI yazilir. Duz metin yalnizca bellekte tutulur.
        public string TruenasApiKeyProtected { get; set; } = "";

        [JsonIgnore] public string TruenasApiKey { get; set; } = "";

        /// <summary>Cozulemeyen bir anahtar vardi — kullaniciya bildirilmeli.</summary>
        [JsonIgnore] public bool SecretUnreadable { get; private set; }
```

Sınıfa iki metot ekle (`Save()`'in hemen üstüne):
```csharp
        /// <summary>Yuklemeden sonra: sifreliyi coz, eski duz metni goc ettir.</summary>
        public bool UnprotectSecrets(KontroXXL.Core.Security.ISecretProtector protector)
        {
            bool changed = false;
            SecretUnreadable = false;

            // Faz 1 oncesi dosyalarda anahtar duz metin "TruenasApiKey" alanindaydi;
            // artik [JsonIgnore] oldugu icin _extra'ya dusuyor. Oradan al ve sifrele.
            if (_extra != null && _extra.TryGetValue("TruenasApiKey", out var legacy))
            {
                string plain = legacy?.ToString() ?? "";
                _extra.Remove("TruenasApiKey");
                if (!string.IsNullOrEmpty(plain))
                {
                    TruenasApiKey = plain;
                    TruenasApiKeyProtected = protector.Protect(plain);
                    changed = true;
                }
            }
            else if (!string.IsNullOrEmpty(TruenasApiKeyProtected))
            {
                string plain = protector.Unprotect(TruenasApiKeyProtected);
                if (plain == null) { SecretUnreadable = true; TruenasApiKey = ""; }
                else TruenasApiKey = plain;
            }

            return changed;
        }

        /// <summary>Kaydetmeden once: bellekteki duz metni sifreli alana yansit.</summary>
        public void ApplyProtection(KontroXXL.Core.Security.ISecretProtector protector)
            => TruenasApiKeyProtected = protector.Protect(TruenasApiKey);
```

- [x] **Step 7: Uygulamayı bağla**

`TrayApplicationContext.cs`, Task 1'de eklediğin `config = AppConfig.Load(...)` bloğundan hemen sonra:
```csharp
                secrets = new DpapiSecretProtector();
                if (config.UnprotectSecrets(secrets)) { config.MarkDirty(); log.Info("API anahtari sifrelendi (goc)."); }
                if (config.SecretUnreadable) log.Info("API anahtari cozulemedi — Ayarlar'dan yeniden girilmeli.");
```
Alan: `private KontroXXL.Core.Security.ISecretProtector secrets;`

`MainForm.SaveConfig()` diske yazmadan önce düz metin anahtarın şifreli alana yansıması gerekiyor, ama `MainForm`'un protector'a erişimi yok. Ona bir referans ver.

`MainForm` sınıfına, `private AppConfig config;` alanının yanına:
```csharp
        // Faz 2 (A5): Ayarlar kaydedilirken duz metin anahtari sifreli alana yansitmak icin.
        public KontroXXL.Core.Security.ISecretProtector Secrets { get; set; }
```

`TrayApplicationContext`'te `mainForm` oluşturulduktan hemen sonra:
```csharp
                mainForm.Secrets = secrets;
```
`ShowMainForm()` içindeki yeniden oluşturma dalına da aynı atamayı ekle — form kapatılıp yeniden açıldığında referans kaybolmasın.

`MainForm.SaveConfig()` içinde, `config.TruenasApiKey = txtNasKey.Text;` satırından hemen sonra:
```csharp
            if (Secrets != null) config.ApplyProtection(Secrets);
```

`SecretUnreadable` doluysa Ayarlar sekmesinde API alanının altına kırmızı bir uyarı etiketi göster: `"⚠ Kayıtlı anahtar çözülemedi, lütfen yeniden girin."`

- [x] **Step 8: Derle, test et, gözle doğrula**

Run: `dotnet build KontroXXL.sln -c Release && dotnet test`
Expected: yeşil, 165 test.

**Kabul (spec §8.6):** Uygulamayı çalıştır, Ayarlar'dan bir API anahtarı gir, kaydet, çık.
`%APPDATA%\KontroXXL\config.json` dosyasını **aç ve gözle bak** — `TruenasApiKeyProtected`
base64 olmalı, girdiğin anahtar **hiçbir yerde düz metin geçmemeli**.

- [x] **Step 9: Commit**

```bash
git add src/KontroXXL.Core/Security tests/KontroXXL.Core.Tests/Security src/KontroXXL_WinApp/DpapiSecretProtector.cs src/KontroXXL_WinApp/Models.cs src/KontroXXL_WinApp/TrayApplicationContext.cs src/KontroXXL_WinApp/MainForm.cs src/KontroXXL_WinApp/KontroXXL_WinApp.csproj
git commit -m "feat(security): encrypt the TrueNAS API key with DPAPI, migrate plaintext (A5)"
```

---

## Task 3: Faz 1 devir maddeleri (A9, D1, D2, D3, D4)

**Files:**
- Modify: `src/KontroXXL_WinApp/Models.cs`, `src/KontroXXL_WinApp/SerialLink.cs`, `src/KontroXXL_WinApp/MainForm.cs`, `src/KontroXXL.Core/Logging/RollingFileLogger.cs`, `src/KontroXXL.Core/Configuration/JsonFileStore.cs`
- Test: `tests/KontroXXL.Core.Tests/Logging/RollingFileLoggerTests.cs`, `tests/KontroXXL.Core.Tests/Configuration/JsonFileStoreTests.cs`

**Interfaces:**
- Consumes: Task 1–2 çıktıları
- Produces: `AppConfig.AutoDetectPort` (bool); `RollingFileLogger` ctor'una `Func<string, StreamWriter>? writerFactory` parametresi

- [x] **Step 1: A9 — `AutoDetectPort` bayrağı**

`Models.cs`, `ArduinoPort` yanına:
```csharp
        // A9: v2'de "COM4" degeri sihirli sekilde "otomatik algila" demekti.
        // Gercekten COM4'teki cihazi olan kullanici surekli eziliyordu.
        public bool AutoDetectPort { get; set; } = true;
```
`ArduinoPort` varsayılanını `"COM4"`'ten `""`'a çevir.

`TrayApplicationContext.InitSerial()` içindeki `SerialLink` kurulumunda:
```csharp
                autoDetect: () => config.AutoDetectPort,
```

`MainForm` Ayarlar sekmesine COM port ComboBox'ının yanına bir CheckBox ekle:
`"Portu otomatik algıla"`, `chkAutoDetectPort`. `LoadConfigToUI`/`SaveConfig`'e bağla.
İşaretliyken ComboBox devre dışı olsun.

- [x] **Step 2: D1 — `_dirty` yarışını kapat**

`Models.cs` `Save()` içindeki serileştirme/yazım bloğunu şununla değiştir:
```csharp
            string json;
            lock (SyncRoot)
            {
                json = JsonConvert.SerializeObject(this, Formatting.Indented);
                // D1: bayragi anlik goruntuyle AYNI kilitte temizle. Ayri bir kilit
                // aliminda temizlemek, disk I/O penceresinde gelen MarkDirty()'yi eziyordu.
                _dirty = false;
            }

            try { KontroXXL.Core.Configuration.JsonFileStore.WriteAtomic(SourcePath, json); }
            catch { lock (SyncRoot) { _dirty = true; } throw; }   // yazim patlarsa flush kaybolmasin
```

- [x] **Step 3: D2 — `.tmp` dosyasının hedefle aynı dizinde olduğunu teste bağla**

`JsonFileStore.cs`'e açıklayıcı yorum ekle:
```csharp
        // D2: gecici dosya HER ZAMAN hedefin yanina yazilir. File.Replace kaynak ve
        // hedefin ayni volume'de olmasini sart kosuyor; boylece bu sart yapisal olarak saglanir.
```

`JsonFileStoreTests.cs`'e ekle:
```csharp
    [Fact]
    public void Temp_file_is_created_beside_the_target_so_File_Replace_stays_same_volume()
    {
        string target = P("a.json");
        JsonFileStore.WriteAtomic(target, "{}");          // hedefi olustur
        JsonFileStore.WriteAtomic(target, "{\"x\":1}");   // File.Replace yolu

        Assert.Equal("{\"x\":1}", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
```

- [x] **Step 4: D3 — reopen hatasını gerçekten tetikleyen test**

`RollingFileLogger` şu anda `StreamWriter`'ı doğrudan kuruyor, bu yüzden reopen hatası
test edilemiyor. Enjekte edilebilir hâle getir. `RollingFileLogger.cs` ctor'unu değiştir:

```csharp
    readonly Func<string, StreamWriter> _writerFactory;

    public RollingFileLogger(string path, LogLevel minLevel = LogLevel.Info,
                             long maxBytes = 1_048_576, int keep = 3,
                             Func<string, StreamWriter>? writerFactory = null)
    {
        _writerFactory = writerFactory ??
            (p => new StreamWriter(p, append: true, Encoding.UTF8) { AutoFlush = false });
        // ... mevcut govde ...
    }
```
`Open()` içindeki `new StreamWriter(...)` çağrısını `_writerFactory(_path)` ile değiştir.

`RollingFileLoggerTests.cs` — mevcut `Keeps_logging_after_a_rotation_whose_reopen_failed`
testini **değiştir**:
```csharp
    [Fact]
    public void Recovers_when_the_post_rotation_reopen_throws()
    {
        int calls = 0;
        StreamWriter Factory(string p)
        {
            calls++;
            // 1. cagri: ctor. 2. cagri: rotasyon sonrasi reopen -> PATLAT.
            // 3. cagri: Write'in retry'i -> basarili olmali.
            if (calls == 2) throw new IOException("simule edilmis kilit");
            return new StreamWriter(p, append: true, System.Text.Encoding.UTF8) { AutoFlush = false };
        }

        using (var log = new RollingFileLogger(Path0, LogLevel.Info, maxBytes: 100, keep: 2, writerFactory: Factory))
        {
            for (int i = 0; i < 10; i++) log.Info(new string('a', 40));   // rotasyonu tetikle
            log.Info("reopen-hatasindan-sonra");
        }

        Assert.True(calls >= 3, $"reopen hic denenmemis, factory cagri sayisi: {calls}");
        string all = string.Join("\n", Directory.GetFiles(_dir).Select(File.ReadAllText));
        Assert.Contains("reopen-hatasindan-sonra", all);
    }
```

**Mutasyon kanıtı zorunlu:** `Write`'taki retry bloğunu geçici olarak kaldır
(`if (_writer is null) return;` hâline getir), testin **kırmızıya döndüğünü** göster,
sonra geri al ve yeşili doğrula. İki çıktıyı da raporla.

- [x] **Step 5: D4 — kaydetme hatasını kullanıcıya göster**

`MainForm.SaveConfig()` içindeki `config.Save();` çağrısını sar:
```csharp
            try { config.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show("Yapılandırma kaydedilemedi:\n\n" + ex.Message,
                    "KontroXXL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;   // "basariyla kaydedildi" mesajini gosterme
            }
```

- [x] **Step 6: Derle, test et, commit**

Run: `dotnet build KontroXXL.sln -c Release && dotnet test`
Expected: yeşil, 166 test.

```bash
git add -u
git commit -m "fix: close phase-1 carry-overs — auto-detect flag, dirty race, reopen test, save errors (A9, D1-D4)"
```

---

## Task 4: Sürümleme ve yayın profili

**Files:** `Directory.Build.props`, `src/KontroXXL_WinApp/KontroXXL_WinApp.csproj`, `installer/publish.ps1`

- [x] **Step 1:** `Directory.Build.props`'ta `<Version>2.2.0</Version>`; `<AssemblyVersion>`/`<FileVersion>` de aynı değere bağla.
- [x] **Step 2:** Ayarlar sekmesine "Hakkında" satırı: `typeof(Program).Assembly.GetName().Version` gösterilsin — spec §8.9'un tek-sürüm kriteri.
- [x] **Step 3:** `installer/publish.ps1`:
```powershell
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "..\publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$PSScriptRoot\..\src\KontroXXL_WinApp\KontroXXL_WinApp.csproj" `
    -c Release -r win-x64 --self-contained false -o $out
Write-Host "Yayin hazir: $out"
```
- [x] **Step 4:** Çalıştır, `publish/` içinde `KontroXXL_WinApp.exe` oluştuğunu doğrula. `publish/` `.gitignore`'da (Faz 1'de eklendi) — kontrol et.
- [x] **Step 5:** Commit.

---

## Task 5: Velopack entegrasyonu

**Files:** `src/KontroXXL_WinApp/Program.cs`, `src/KontroXXL_WinApp/KontroXXL_WinApp.csproj`, `src/KontroXXL_WinApp/TrayApplicationContext.cs`, `installer/pack.ps1`

**KRİTİK:** `VelopackApp.Build().Run();` `Main`'in **ilk satırı** olmalı — mutex'ten de önce.
Velopack kurulum/güncelleme hook'ları bu çağrıda çalışır ve process'i sonlandırabilir.
Yanlış sırada kurulum sessizce bozulur (spec §9).

- [x] **Step 1:** `dotnet add src/KontroXXL_WinApp package Velopack`
- [x] **Step 2:** `Program.Main`'in ilk satırı:
```csharp
            // Velopack kurulum/guncelleme hook'lari — mutex dahil HER SEYDEN once.
            Velopack.VelopackApp.Build().Run();
```
- [x] **Step 3:** Tray menüsüne "Güncellemeleri Denetle" ekle:
```csharp
                cms.Items.Add("Güncellemeleri Denetle", null, async (s, e) => await CheckUpdatesAsync());
```
```csharp
        private async Task CheckUpdatesAsync()
        {
            try
            {
                var mgr = new Velopack.UpdateManager(
                    new Velopack.Sources.GithubSource(UpdateFeedUrl, null, false));
                var newVer = await mgr.CheckForUpdatesAsync();
                if (newVer == null)
                {
                    MessageBox.Show("Zaten güncelsiniz.", "KontroXXL");
                    return;
                }
                if (MessageBox.Show($"Yeni sürüm: {newVer.TargetFullRelease.Version}\nŞimdi güncellensin mi?",
                        "KontroXXL", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

                await mgr.DownloadUpdatesAsync(newVer);
                config.FlushIfDirty();
                mgr.ApplyUpdatesAndRestart(newVer);
            }
            catch (Exception ex) { log.Error("Guncelleme denetimi hatasi", ex); }
        }
```
`UpdateFeedUrl` sabitini Task 7'de gerçek depo adresiyle doldur; o zamana kadar `const string UpdateFeedUrl = "";` ve boşsa metot kullanıcıya "güncelleme kaynağı yapılandırılmamış" desin.

- [x] **Step 4:** `installer/pack.ps1`:
```powershell
$ErrorActionPreference = "Stop"
& "$PSScriptRoot\publish.ps1"
$ver = ([xml](Get-Content "$PSScriptRoot\..\Directory.Build.props")).Project.PropertyGroup.Version
vpk pack --packId KontroXXL --packVersion $ver `
         --packDir "$PSScriptRoot\..\publish" `
         --mainExe KontroXXL_WinApp.exe `
         --icon "$PSScriptRoot\..\icon.ico" `
         --outputDir "$PSScriptRoot\..\releases"
```
- [x] **Step 5:** `dotnet tool install -g vpk`, sonra `installer/pack.ps1` çalıştır. `releases/` altında `KontroXXL-win-Setup.exe` oluşmalı. `releases/` `.gitignore`'a ekle.
- [x] **Step 6:** Commit.

**Kabul (spec §8.3, §8.4, §8.8):** Kurulum exe'sini çift tıkla çalıştır → Başlat menüsünde kısayol, uygulama açılıyor, `%APPDATA%\KontroXXL\config.json` oluşuyor, kurulum dizinine hiçbir şey yazılmıyor. Kaldır → kısayol gidiyor, `%APPDATA%\KontroXXL\` **kalıyor**. **Bu adımlar Karaduman'a borçlu.**

---

## Task 6: Arduino firmware yükleme

**Files:** `src/KontroXXL_WinApp/FirmwareFlasher.cs`, `src/KontroXXL_WinApp/MainForm.cs`, `src/KontroXXL_WinApp/TrayApplicationContext.cs`, `installer/tools/avrdude/`, `firmware/arduino_kontrol.ino.hex`

**Ön koşul:** Karaduman'ın export ettiği `.hex` deposunda olmalı. Yoksa görev BLOKE.

- [ ] **Step 1:** avrdude'u kopyala:
```bash
mkdir -p installer/tools/avrdude/bin installer/tools/avrdude/etc
cp "$LOCALAPPDATA/Arduino15/packages/arduino/tools/avrdude/8.0.0-arduino1/bin/avrdude.exe" installer/tools/avrdude/bin/
cp "$LOCALAPPDATA/Arduino15/packages/arduino/tools/avrdude/8.0.0-arduino1/etc/avrdude.conf" installer/tools/avrdude/etc/
```
- [ ] **Step 2:** `.csproj`'e içerik kopyalama kuralı ekle (avrdude + `.hex` çıktı klasörüne gitsin, `CopyToOutputDirectory=PreserveNewest`).
- [ ] **Step 3:** `FirmwareFlasher.cs` — `Task<(bool ok, string output)> FlashAsync(string port, string hexPath, string avrdudePath, string confPath)`. avrdude'u `RedirectStandardOutput`+`RedirectStandardError` ile çalıştır, 60 sn timeout, çıktıyı biriktir.
  **Güvenlik:** çağırmadan önce `SerialLink.DetectArduinoPort` ile hedef portun Arduino/CH340/CP210x olduğunu doğrula; değilse `false` dön ve sebebini yaz.
  Komut: `-C <conf> -p atmega328p -c arduino -P <port> -b 115200 -D -U flash:w:<hex>:i`
- [ ] **Step 4:** Ayarlar sekmesine **"Arduino'yu Programla"** düğmesi. Akış: onay diyaloğu → `serial.Stop()` → `FlashAsync` → çıktıyı kaydırılabilir bir diyalogda göster → `serial.Start()`.
- [ ] **Step 5:** Derle, test et (test sayısı değişmez), commit.

**Kabul (spec §8.7):** Düğmeye bas, LCD firmware'i yüklensin, sonrasında LCD normal çalışsın. **Karaduman'a borçlu.**

---

## Task 7: Sır taraması ve GitHub deposu

**Bu görev iki ayrı onay kapısı içerir. Onaysız hiçbir dışa açılan işlem yapılmaz.**

- [ ] **Step 1: Sır taraması — onaydan ÖNCE, zorunlu**

```bash
cd "C:/Users/ibrahimk/Desktop/nas-lcd"

echo "=== 1) gecmise hic riskli dosya girdi mi ==="
git log --all --pretty=format: --name-only --diff-filter=A | sort -u \
  | grep -iE "config\.json|\.log$|Release_v2|crash" || echo "TEMIZ"

echo "=== 2) mevcut config.json'daki gercek anahtar gecmiste geciyor mu ==="
# Deseni tahmin etme — diskteki gercek anahtari oku ve ONU ara.
KEY=$(python -c "import json;print(json.load(open('config.json')).get('TruenasApiKey',''))" 2>/dev/null)
if [ -n "$KEY" ]; then
  git log --all -p -S"$KEY" --oneline || echo "TEMIZ - anahtar hicbir commit'te yok"
else
  echo "config.json'da duz metin anahtar yok (Task 2 sifreledi) - eski kopyalari da kontrol et"
fi

echo "=== 3) genel yuksek-entropi taramasi (elle degerlendirilecek) ==="
git log --all -p | grep -nE "ApiKey|api_key|Bearer [A-Za-z0-9]" | head -20 || echo "eslesme yok"
```

1 ve 2 **temiz olmak zorunda**. 3'ün çıktısı sayı değil, **elle okunacak bir liste** —
kaynak kodda `TruenasApiKey` alan adının geçmesi normaldir, gerçek bir anahtar değeri
görünmesi değildir. Farkı gözle ayır.

**Herhangi biri kirli çıkarsa dur ve bildir** — geçmiş temizlenmeden push edilmez.

- [ ] **Step 2: ONAY KAPISI 1** — depo adı ve görünürlük (public/private) Karaduman'a sorulur. Cevap alınmadan devam edilmez.

- [ ] **Step 3:** `gh repo create <ad> --<public|private> --source=. --remote=origin` ve **ONAY KAPISI 2** sonrası `git push -u origin main` + `git push --tags`.

- [ ] **Step 4:** Task 5'teki `UpdateFeedUrl` sabitini gerçek depo adresiyle doldur, commit.

- [ ] **Step 5:** İlk release: `gh release create v2.2.0 releases/* --title "KontroXXL 2.2.0" --notes "..."`. Ayrı onay.

---

## Task 8: Dokümanlar ve kabul

- [x] **Step 1:** `README.md` — kurulum bölümü (Setup exe'si, SmartScreen uyarısı beklenir), `%APPDATA%` yolları, `config.json` elle düzenlemeden önce uygulamayı kapatma uyarısı (Faz 1'de eklendi, yol değiştiği için güncelle).
- [x] **Step 2:** `DOCS.md` — §2 dosya haritasına `installer/`, `firmware/*.hex`; yeni bir "§12 Kurulum ve güncelleme" bölümü; `%APPDATA%` yolları; DPAPI notu.
- [x] **Step 3:** `TODO.md` — "Config Şifreleme" ve "Versiyon Kontrolü" maddelerini işaretle; kalan Faz 3/4 maddelerini güncelle.
- [x] **Step 4:** Sürüm numaralarını 2.2.0'da tekleştir (spec dosyalarına dokunma — onlar tarihli anlık görüntü).
- [x] **Step 5:** Spec §8'deki 9 kabul kriterini sırayla geç; koşulamayanları açıkça "Karaduman'a borçlu" diye işaretle. Sahte onay yok.
- [x] **Step 6:** Commit; etiketleme controller'a ait.

---

## Faz 2 sonunda ne olacak

- `KontroXXL-win-Setup.exe` çift tıkla kuruyor, UAC istemi çıkmıyor.
- Uygulama kendini GitHub Releases üzerinden güncelliyor.
- API anahtarı diskte DPAPI ile şifreli; düz metin hiçbir yerde yok.
- Ayarlar ve kısayollar `%APPDATA%\KontroXXL\` altında, güncellemeden etkilenmiyor; v2 kurulumundan otomatik göç ediyor.
- Ayarlar'daki bir düğme Arduino'yu programlıyor — Arduino IDE gerekmiyor.
- Faz 1'den devreden beş açık madde (D1–D4, A9) kapalı.

**Sonraki:** Faz 3 (mimari — tanrı sınıfın bölünmesi, tipli DTO'lar, LibreHardwareMonitor, NAudio) planı Faz 2 tamamlandıktan sonra yazılacak.
