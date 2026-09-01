# KontroXXL Faz 1 — Sağlamlaştırma Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uygulamanın saatler içinde `OutOfMemoryException` ile çökmesini, Arduino kopunca ölü kalmasını ve log dosyasının sonsuza kadar büyümesini bitirmek; LCD mantığını test edilebilir bir çekirdeğe taşımak.

**Architecture:** Yeni bir `KontroXXL.Core` sınıf kütüphanesi (net8.0, hiçbir Windows API'si yok) saf mantığı barındırır: metin sanitizasyonu, log rotasyonu, LCD durum makinesi, LCD çerçeve üretimi, seri satır tamponu, yapılandırma deposu. Mevcut `TrayApplicationContext` bu parçaların **tüketicisi** hâline gelir; WinForms arayüzüne yalnızca sızıntıyı durduran minimum müdahale yapılır (Faz 4 onu zaten siliyor).

**Tech Stack:** C# 12, .NET 8.0 (SDK 8.0.301), xUnit, WinForms (geçici), Newtonsoft.Json (Faz 3'te değişecek)

**Spec:** `docs/superpowers/specs/2026-09-01-kontroxxl-v3.md`

## Global Constraints

- `KontroXXL.Core` **`net8.0`** hedefler ve şu referansları **asla** almaz: `System.Windows.Forms`, `System.IO.Ports`, `System.Management`, `Microsoft.Win32.Registry`, `AudioSwitcher.*`, Avalonia. İhlal edilirse Task 1'deki mimari test kırmızıya döner.
- `KontroXXL_WinApp` **`net8.0-windows`** hedefinde kalır ve `<Nullable>disable</Nullable>` ayarı **değişmez** (açmak mevcut 1850 satırda uyarı seli üretir).
- Yeni projeler (`KontroXXL.Core`, `KontroXXL.Core.Tests`) `<Nullable>enable</Nullable>` kullanır.
- Makinede yalnızca SDK **8.0.301** kurulu. Hiçbir `TargetFramework` `net8.0`'ın üstüne çıkarılmaz.
- LCD'ye giden her satır **tam olarak 16 karakter** olmalıdır. Yalnızca ASCII 0x20–0x7E ve iki özel bayt: `\x01` (RX oku), `\x02` (TX oku).
- Seri protokol v2'den **değişmez**: `L0=`, `L1=`, `B1=`, `CLR`, `ON`, `OFF` / `EV:UP`, `EV:DN`, `EV:CLICK`, `EV:BACK`, `CMD:READY`, `CMD:UPDATE`, `CMD:APPS`, `CMD:POOLS`, `CMD:SHORTCUTS`. Arduino firmware'i bu fazda **hiç değiştirilmez**.
- Kullanıcıya görünen metinler Türkçe; kod tanımlayıcıları İngilizce (mevcut stil).
- Her commit'ten önce `dotnet build KontroXXL.sln -c Debug` ve `dotnet test` yeşil olmalıdır.
- Çalışma dizini: `C:\Users\ibrahimk\Desktop\nas-lcd`

---

## File Structure

**Oluşturulacak:**

| Dosya | Sorumluluk |
|---|---|
| `.gitignore` | Build çıktıları, loglar, sırlar |
| `KontroXXL.sln` | Üç proje |
| `Directory.Build.props` | Ortak `LangVersion`, `Version` |
| `src/KontroXXL.Core/KontroXXL.Core.csproj` | Saf mantık kütüphanesi |
| `src/KontroXXL.Core/Lcd/LcdText.cs` | Türkçe→ASCII translit + 16 karaktere sabitleme |
| `src/KontroXXL.Core/Lcd/LcdMenuModel.cs` | Menü durum makinesi (saf fonksiyon) |
| `src/KontroXXL.Core/Lcd/LcdFormatter.cs` | Durum + veri → 16×2 çerçeve (saf fonksiyon) |
| `src/KontroXXL.Core/Logging/RollingFileLogger.cs` | Seviyeli, gerçekten dönen dosya logu |
| `src/KontroXXL.Core/Serial/SerialLineBuffer.cs` | Bayt akışı → satır (saf) |
| `src/KontroXXL.Core/Configuration/ConfigStore.cs` | Atomik yazım + debounce'lu flush |
| `src/KontroXXL_WinApp/SerialLink.cs` | Seri port + otomatik yeniden bağlanma (WinForms projesinde) |
| `tests/KontroXXL.Core.Tests/**` | xUnit testleri |

**Değiştirilecek:**

| Dosya | Neden |
|---|---|
| `src/KontroXXL_WinApp/TrayApplicationContext.cs` | Core parçalarını tüket, üç timer'a böl, SerialLink'e geç |
| `src/KontroXXL_WinApp/Models.cs` | Yeni interval alanları, `ConfigStore`'a devir |
| `src/KontroXXL_WinApp/MainForm.cs` | Dispose + statik font cache |
| `DOCS.md`, `TODO.md`, `PROJECT_SUMMARY.md` | Gerçeğe hizalama |

**Not:** `KontroXXL_WinApp/` klasörü Task 1'de `src/` altına taşınır. Proje **adı** değişmez (Faz 4'te zaten siliniyor).

---

## Task 1: Depo, solution ve test iskeleti

**Files:**
- Create: `.gitignore`, `Directory.Build.props`, `KontroXXL.sln`
- Create: `src/KontroXXL.Core/KontroXXL.Core.csproj`
- Create: `tests/KontroXXL.Core.Tests/KontroXXL.Core.Tests.csproj`
- Create: `tests/KontroXXL.Core.Tests/ArchitectureTests.cs`
- Move: `KontroXXL_WinApp/` → `src/KontroXXL_WinApp/`

**Interfaces:**
- Consumes: yok (ilk görev)
- Produces: `KontroXXL.Core` assembly'si ve çalışan `dotnet test` komutu. Sonraki tüm görevler `namespace KontroXXL.Core.*` altına yazar.

- [ ] **Step 1: Depoyu başlat ve `.gitignore` yaz**

`.gitignore`:
```gitignore
# Build
[Bb]in/
[Oo]bj/
*.user
*.suo
.vs/

# Yayın çıktıları — kaynaktan üretilir
Release_v2/
publish/
*.hex

# Loglar ve runtime durumu — SIR İÇEREBİLİR
*.log
*.log.bak
app.log*
crash.log
build.txt
err.txt
errors.txt
errors.log
publish_out.txt
publish_final.txt

# Yapılandırma — API anahtarı içerir, ASLA commit edilmez
config.json

# Ajan/oturum durumu
.remember/
.claude/settings.local.json
.superpowers/
```

- [ ] **Step 2: Sırları depodan uzak tutarak ilk commit'i at**

```bash
cd "C:/Users/ibrahimk/Desktop/nas-lcd"
git init
git add .gitignore
git commit -m "chore: add gitignore before first import (config.json holds a live API key)"
git add .
git status --short
```

`git status --short` çıktısında `config.json` veya `app.log` **görünmemelidir**. Görünüyorsa `.gitignore`'u düzelt, `git rm --cached <dosya>` uygula.

```bash
git commit -m "chore: import KontroXXL v2 as baseline"
```

- [ ] **Step 3: WinForms projesini `src/` altına taşı**

```bash
git mv KontroXXL_WinApp src/KontroXXL_WinApp
git mv arduino_kontrol firmware/arduino_kontrol
git mv "eski versiyon.ino" firmware/eski-versiyon.ino.txt
git commit -m "chore: move projects under src/ and firmware/"
```

- [ ] **Step 4: `Directory.Build.props` oluştur**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
    <Version>2.1.0</Version>
    <Company>KontroXXL</Company>
    <Product>KontroXXL</Product>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Core ve test projelerini oluştur, solution'a bağla**

```bash
cd "C:/Users/ibrahimk/Desktop/nas-lcd"
dotnet new sln -n KontroXXL
dotnet new classlib -n KontroXXL.Core -o src/KontroXXL.Core -f net8.0
dotnet new xunit  -n KontroXXL.Core.Tests -o tests/KontroXXL.Core.Tests -f net8.0
rm src/KontroXXL.Core/Class1.cs tests/KontroXXL.Core.Tests/UnitTest1.cs
dotnet sln add src/KontroXXL.Core/KontroXXL.Core.csproj
dotnet sln add tests/KontroXXL.Core.Tests/KontroXXL.Core.Tests.csproj
dotnet sln add src/KontroXXL_WinApp/KontroXXL_WinApp.csproj
dotnet add tests/KontroXXL.Core.Tests reference src/KontroXXL.Core
dotnet add src/KontroXXL_WinApp reference src/KontroXXL.Core
```

`src/KontroXXL.Core/KontroXXL.Core.csproj` içindeki `PropertyGroup`'a ekle:
```xml
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

- [ ] **Step 6: Katman kuralını zorlayan mimari testini yaz (başarısız olmalı değil — geçmeli)**

`tests/KontroXXL.Core.Tests/ArchitectureTests.cs`:
```csharp
using System.Linq;
using System.Reflection;
using KontroXXL.Core;
using Xunit;

namespace KontroXXL.Core.Tests;

public class ArchitectureTests
{
    // Core'un bağımlılık listesi spec bölüm 4.1'de kilitli.
    // Bu test, birinin Core'a Windows API'si sızdırmasını derleme sonrası yakalar.
    static readonly string[] Forbidden =
    {
        "System.Windows.Forms",
        "System.IO.Ports",
        "System.Management",
        "Microsoft.Win32.Registry",
        "AudioSwitcher.AudioApi",
        "Avalonia",
    };

    [Fact]
    public void Core_does_not_reference_platform_assemblies()
    {
        var core = typeof(CoreMarker).Assembly;
        var referenced = core.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        var violations = referenced.Where(r => Forbidden.Contains(r)).ToArray();

        Assert.Empty(violations);
    }
}
```

`src/KontroXXL.Core/CoreMarker.cs`:
```csharp
namespace KontroXXL.Core;

/// <summary>Assembly'yi testlerden bulmak için sabit tutamak. Başka amacı yok.</summary>
public static class CoreMarker { }
```

- [ ] **Step 7: Testleri koştur**

Run: `dotnet test`
Expected: PASS — 1 test.

- [ ] **Step 8: Commit**

```bash
git add .
git commit -m "chore: add KontroXXL.Core library, xUnit test project and layering test"
```

---

## Task 2: `LcdText` — Türkçe translit ve 16 karakter garantisi (A10)

**Files:**
- Create: `src/KontroXXL.Core/Lcd/LcdText.cs`
- Test: `tests/KontroXXL.Core.Tests/Lcd/LcdTextTests.cs`

**Interfaces:**
- Consumes: yok
- Produces:
  - `static string LcdText.Sanitize(string? text)` — Türkçe harfleri translitere eder, ASCII dışını `'?'` yapar, `\x01`/`\x02` özel baytlarını korur. Uzunluğu **değiştirmez**.
  - `static string LcdText.Fit(string? text, int width = 16)` — sanitize eder, sonra tam `width` uzunluğuna kırpar/boşlukla doldurur.
  - `static string LcdText.Scroll(string? text, int offset, int width = 16)` — `width`'ten uzun metni `offset` kadar kaydırılmış 16 karakterlik pencere olarak döndürür.

- [ ] **Step 1: Başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Lcd/LcdTextTests.cs`:
```csharp
using KontroXXL.Core.Lcd;
using Xunit;

namespace KontroXXL.Core.Tests.Lcd;

public class LcdTextTests
{
    [Theory]
    [InlineData("Müzik Çalar", "Muzik Calar")]
    [InlineData("İŞĞÜÖÇ", "ISGUOC")]
    [InlineData("ışğüöç", "isguoc")]
    [InlineData("CİFS, SSH", "CIFS, SSH")]
    [InlineData("Obsidian", "Obsidian")]
    public void Sanitize_transliterates_turkish(string input, string expected)
        => Assert.Equal(expected, LcdText.Sanitize(input));

    [Fact]
    public void Sanitize_preserves_custom_arrow_bytes()
        => Assert.Equal("\x01 12Mb \x02 34Mb", LcdText.Sanitize("\x01 12Mb \x02 34Mb"));

    [Fact]
    public void Sanitize_replaces_other_non_ascii_with_question_mark()
        => Assert.Equal("a?b", LcdText.Sanitize("a\u20acb")); // euro sign

    [Fact]
    public void Sanitize_never_changes_length()
        => Assert.Equal(11, LcdText.Sanitize("Müzik Çalar").Length);

    [Fact]
    public void Sanitize_of_null_is_empty()
        => Assert.Equal("", LcdText.Sanitize(null));

    [Fact]
    public void Fit_pads_short_text_to_16()
        => Assert.Equal("CPU:76%         ", LcdText.Fit("CPU:76%"));

    [Fact]
    public void Fit_truncates_long_text_to_16()
        => Assert.Equal("0123456789ABCDEF", LcdText.Fit("0123456789ABCDEF_TASMA"));

    [Fact]
    public void Fit_always_returns_exactly_16()
    {
        Assert.Equal(16, LcdText.Fit("").Length);
        Assert.Equal(16, LcdText.Fit(null).Length);
        Assert.Equal(16, LcdText.Fit("kısa").Length);
        Assert.Equal(16, LcdText.Fit(new string('x', 100)).Length);
    }

    [Fact]
    public void Scroll_returns_text_unchanged_when_it_fits()
        => Assert.Equal("kisa            ", LcdText.Scroll("kısa", offset: 7));

    [Fact]
    public void Scroll_shifts_long_text_by_offset()
    {
        // "ABCDEFGHIJKLMNOPQRS" (19) -> kaydırma penceresi "  " ile birleştirilmiş metin üzerinde
        var s = LcdText.Scroll("ABCDEFGHIJKLMNOPQRS", offset: 3);
        Assert.Equal(16, s.Length);
        Assert.Equal("DEFGHIJKLMNOPQRS", s);
    }

    [Fact]
    public void Scroll_wraps_around_without_throwing()
    {
        for (int i = 0; i < 200; i++)
            Assert.Equal(16, LcdText.Scroll("ABCDEFGHIJKLMNOPQRS", i).Length);
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter LcdTextTests`
Expected: FAIL — `LcdText` tipi bulunamıyor (CS0246).

- [ ] **Step 3: `LcdText`'i yaz**

`src/KontroXXL.Core/Lcd/LcdText.cs`:
```csharp
using System.Text;

namespace KontroXXL.Core.Lcd;

/// <summary>
/// HD44780 LCD 16x2 ekranına giden her metin buradan geçer.
/// Ekran yalnızca ASCII 0x20-0x7E ile iki özel karakteri (0x01 RX oku, 0x02 TX oku) çizebilir.
/// </summary>
public static class LcdText
{
    public const int Width = 16;
    public const char RxArrow = '\x01';
    public const char TxArrow = '\x02';

    // Türkçe harfler için birebir karşılıklar. Sıra önemli değil, uzunluk 1:1 korunur.
    static readonly Dictionary<char, char> Translit = new()
    {
        ['ı'] = 'i', ['İ'] = 'I',
        ['ğ'] = 'g', ['Ğ'] = 'G',
        ['ü'] = 'u', ['Ü'] = 'U',
        ['ş'] = 's', ['Ş'] = 'S',
        ['ö'] = 'o', ['Ö'] = 'O',
        ['ç'] = 'c', ['Ç'] = 'C',
    };

    /// <summary>Uzunluğu değiştirmeden ekranın çizebileceği karakterlere indirger.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == RxArrow || c == TxArrow) { sb.Append(c); continue; }
            if (Translit.TryGetValue(c, out char mapped)) { sb.Append(mapped); continue; }
            sb.Append(c >= 0x20 && c <= 0x7E ? c : '?');
        }
        return sb.ToString();
    }

    /// <summary>Sanitize eder ve tam olarak <paramref name="width"/> karakter döndürür.</summary>
    public static string Fit(string? text, int width = Width)
    {
        string s = Sanitize(text);
        return s.Length >= width ? s[..width] : s.PadRight(width);
    }

    /// <summary>
    /// Ekrana sığmayan metni kaydırarak gösterir. <paramref name="offset"/> çağıran tarafından
    /// artırılır — böylece fonksiyon saf kalır ve testte zamana bağımlı olmaz.
    /// </summary>
    public static string Scroll(string? text, int offset, int width = Width)
    {
        string s = Sanitize(text);
        if (s.Length <= width) return s.PadRight(width);

        string extended = s + "  " + s;
        int period = s.Length + 2;
        int start = ((offset % period) + period) % period;
        return extended.Substring(start, width);
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter LcdTextTests`
Expected: PASS — 12 test.

- [ ] **Step 5: Commit**

```bash
git add src/KontroXXL.Core/Lcd/LcdText.cs tests/KontroXXL.Core.Tests/Lcd/LcdTextTests.cs
git commit -m "feat(lcd): add LcdText with Turkish transliteration and fixed-width fitting (A10)"
```

---

## Task 3: `RollingFileLogger` — gerçekten dönen log (A3)

**Files:**
- Create: `src/KontroXXL.Core/Logging/ILog.cs`
- Create: `src/KontroXXL.Core/Logging/RollingFileLogger.cs`
- Test: `tests/KontroXXL.Core.Tests/Logging/RollingFileLoggerTests.cs`
- Modify: `src/KontroXXL_WinApp/TrayApplicationContext.cs:61-62,74-79,159-166` (inline `Log` yerine)

**Interfaces:**
- Consumes: yok
- Produces:
  - `enum LogLevel { Debug = 0, Info = 1, Error = 2 }`
  - `interface ILog { void Debug(string msg); void Info(string msg); void Error(string msg, Exception? ex = null); }`
  - `sealed class RollingFileLogger : ILog, IDisposable` — ctor `(string path, LogLevel minLevel = LogLevel.Info, long maxBytes = 1_048_576, int keep = 3)`

- [ ] **Step 1: Başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Logging/RollingFileLoggerTests.cs`:
```csharp
using KontroXXL.Core.Logging;
using Xunit;

namespace KontroXXL.Core.Tests.Logging;

public class RollingFileLoggerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "kx-log-" + Guid.NewGuid().ToString("N"));

    public RollingFileLoggerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string Path0 => Path.Combine(_dir, "app.log");

    [Fact]
    public void Info_writes_a_timestamped_line()
    {
        using (var log = new RollingFileLogger(Path0))
            log.Info("merhaba");

        string content = File.ReadAllText(Path0);
        Assert.Contains("merhaba", content);
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] \[INF\] merhaba", content);
    }

    [Fact]
    public void Debug_is_suppressed_below_min_level()
    {
        using (var log = new RollingFileLogger(Path0, LogLevel.Info))
        {
            log.Debug("gorunmemeli");
            log.Info("gorunmeli");
        }

        string content = File.ReadAllText(Path0);
        Assert.DoesNotContain("gorunmemeli", content);
        Assert.Contains("gorunmeli", content);
    }

    [Fact]
    public void Rotation_moves_current_to_app1_and_truncates_current()
    {
        using (var log = new RollingFileLogger(Path0, LogLevel.Info, maxBytes: 200, keep: 3))
            for (int i = 0; i < 40; i++)
                log.Info(new string('x', 40));

        Assert.True(File.Exists(Path0));
        Assert.True(File.Exists(Path.Combine(_dir, "app.1.log")));
        // Kritik: mevcut dosya kesilmiş olmalı — eski kod bunu yapmıyordu (A3).
        Assert.True(new FileInfo(Path0).Length <= 400,
            $"app.log dondurulmedi, boyut: {new FileInfo(Path0).Length}");
    }

    [Fact]
    public void Rotation_keeps_at_most_the_configured_number_of_archives()
    {
        using (var log = new RollingFileLogger(Path0, LogLevel.Info, maxBytes: 100, keep: 2))
            for (int i = 0; i < 200; i++)
                log.Info(new string('y', 40));

        Assert.True(File.Exists(Path.Combine(_dir, "app.1.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "app.2.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "app.3.log")));
    }

    [Fact]
    public void Creates_missing_directory()
    {
        string nested = Path.Combine(_dir, "a", "b", "app.log");
        using (var log = new RollingFileLogger(nested))
            log.Info("olustu");
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Concurrent_writes_do_not_throw_and_lose_nothing()
    {
        using (var log = new RollingFileLogger(Path0))
            Parallel.For(0, 200, i => log.Info("satir" + i));

        Assert.Equal(200, File.ReadAllLines(Path0).Length);
    }

    [Fact]
    public void Error_appends_exception_text()
    {
        using (var log = new RollingFileLogger(Path0))
            log.Error("patladi", new InvalidOperationException("sebep"));

        string content = File.ReadAllText(Path0);
        Assert.Contains("patladi", content);
        Assert.Contains("sebep", content);
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter RollingFileLoggerTests`
Expected: FAIL — `RollingFileLogger` bulunamıyor (CS0246).

- [ ] **Step 3: `ILog` ve `RollingFileLogger`'ı yaz**

`src/KontroXXL.Core/Logging/ILog.cs`:
```csharp
namespace KontroXXL.Core.Logging;

public enum LogLevel { Debug = 0, Info = 1, Error = 2 }

public interface ILog
{
    void Debug(string msg);
    void Info(string msg);
    void Error(string msg, Exception? ex = null);
}

/// <summary>Log yazmayan uygulama — testlerde ve log açılamadığında kullanılır.</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Debug(string msg) { }
    public void Info(string msg) { }
    public void Error(string msg, Exception? ex = null) { }
}
```

`src/KontroXXL.Core/Logging/RollingFileLogger.cs`:
```csharp
using System.Text;

namespace KontroXXL.Core.Logging;

/// <summary>
/// Boyut sınırına gelince gerçekten dönen dosya logu.
/// v2'deki hata (A3): eski kod .bak'a KOPYALIYOR, orijinali kesmiyordu — dosya sonsuz büyüyordu.
/// Burada döndürme sırası: app.2.log -> app.3.log, app.1.log -> app.2.log, app.log -> app.1.log, yeni app.log.
/// </summary>
public sealed class RollingFileLogger : ILog, IDisposable
{
    readonly string _path;
    readonly string _dir;
    readonly string _stem;      // "app"
    readonly string _ext;       // ".log"
    readonly long _maxBytes;
    readonly int _keep;
    readonly LogLevel _minLevel;
    readonly object _gate = new();

    StreamWriter? _writer;
    long _size;
    bool _disposed;

    public RollingFileLogger(string path, LogLevel minLevel = LogLevel.Info,
                             long maxBytes = 1_048_576, int keep = 3)
    {
        _path = path;
        _dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        _stem = Path.GetFileNameWithoutExtension(path);
        _ext = Path.GetExtension(path);
        _minLevel = minLevel;
        _maxBytes = maxBytes;
        _keep = Math.Max(1, keep);

        Directory.CreateDirectory(_dir);
        Open();
    }

    public void Debug(string msg) => Write(LogLevel.Debug, "DBG", msg);
    public void Info(string msg) => Write(LogLevel.Info, "INF", msg);

    public void Error(string msg, Exception? ex = null)
        => Write(LogLevel.Error, "ERR", ex is null ? msg : $"{msg} :: {ex}");

    void Write(LogLevel level, string tag, string msg)
    {
        if (level < _minLevel || _disposed) return;

        string line = $"[{DateTime.Now:HH:mm:ss}] [{tag}] {msg}";
        lock (_gate)
        {
            if (_writer is null) return;
            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
                _size += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (_size >= _maxBytes) Rotate();
            }
            catch
            {
                // Log yazamamak uygulamayı düşürmemeli.
            }
        }
    }

    void Open()
    {
        var fi = new FileInfo(_path);
        _size = fi.Exists ? fi.Length : 0;
        _writer = new StreamWriter(_path, append: true, Encoding.UTF8) { AutoFlush = false };
    }

    string Archive(int n) => Path.Combine(_dir, $"{_stem}.{n}{_ext}");

    void Rotate()
    {
        _writer?.Dispose();
        _writer = null;

        try
        {
            // En eskiyi at, kalanları bir kaydır.
            string oldest = Archive(_keep);
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int n = _keep - 1; n >= 1; n--)
                if (File.Exists(Archive(n)))
                    File.Move(Archive(n), Archive(n + 1), overwrite: true);

            if (File.Exists(_path))
                File.Move(_path, Archive(1), overwrite: true);
        }
        catch
        {
            // Döndürme başarısızsa loglamaya devam et; dosya büyür ama uygulama yaşar.
        }

        Open();
        _size = 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter RollingFileLoggerTests`
Expected: PASS — 7 test.

- [ ] **Step 5: `TrayApplicationContext`'i yeni logger'a bağla**

`src/KontroXXL_WinApp/TrayApplicationContext.cs` içinde:

`61-62` satırlarındaki alanları değiştir:
```csharp
        private ILog log = NullLog.Instance;
```
(`private string logPath ...` ve `private StreamWriter _logWriter;` satırlarını **sil**.)

`74-79` satırlarındaki rotasyon bloğunu değiştir:
```csharp
            try {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
                log = new RollingFileLogger(logFile, LogLevel.Info);
            } catch { log = NullLog.Instance; }
```

`159-166` satırlarındaki `Log` metodunu değiştir:
```csharp
        private void Log(string msg) => log.Info(msg);
```

`using KontroXXL.Core.Logging;` ekle.

`252` satırındaki gürültülü seri trafiği `Debug`'a indir — bu tek satır log boyutunu 10 kattan fazla düşürür:
```csharp
                        log.Debug("Arduino'dan gelen: " + line);
```

- [ ] **Step 6: Derle ve çalıştır**

Run: `dotnet build KontroXXL.sln -c Debug`
Expected: hata yok.

Run: `dotnet run --project src/KontroXXL_WinApp` — uygulama açılmalı, tray ikonu görünmeli, `Release_v2` yerine `bin/Debug/net8.0-windows/app.log` oluşmalı ve içinde `[INF]` etiketli satırlar bulunmalı. Kapat.

- [ ] **Step 7: Commit**

```bash
git add src/KontroXXL.Core/Logging tests/KontroXXL.Core.Tests/Logging src/KontroXXL_WinApp/TrayApplicationContext.cs
git commit -m "fix(log): real rotation with level filtering, replaces non-truncating copy (A3)"
```

---

## Task 4: `LcdMenuModel` — menü durum makinesi (A7)

**Files:**
- Create: `src/KontroXXL.Core/Lcd/LcdMenuModel.cs`
- Test: `tests/KontroXXL.Core.Tests/Lcd/LcdMenuModelTests.cs`
- Modify: `src/KontroXXL_WinApp/TrayApplicationContext.cs:33-38,318-381` (`HandleArduinoEvent` + `FixIndex` yerine)

**Interfaces:**
- Consumes: yok
- Produces:
  - `enum LcdMode { Home, Menu, Apps, Pools, Shortcuts, NasPower }`
  - `enum LcdInput { Up, Down, Click, Back }`
  - `enum LcdEffect { None, VolumeUp, VolumeDown, RequestSync, ToggleApp, RunShortcut, NasReboot, NasShutdown }`
  - `readonly record struct MenuCounts(int Apps, int Pools, int Shortcuts)`
  - `sealed record LcdMenuState(LcdMode Mode, int Index, int Page)` + `static LcdMenuState Initial`
  - `readonly record struct LcdTransition(LcdMenuState State, LcdEffect Effect, int EffectIndex)`
  - `static LcdTransition LcdMenuModel.Apply(LcdMenuState state, LcdInput input, MenuCounts counts)`

  **Ekran temizleme kuralı:** `LcdEffect` içinde `ClearScreen` **yoktur**. Çağıran, `newState.Mode != old.Mode || newState.Page != old.Page` olduğunda `CLR` gönderir ve tam yeniden çizim yapar.

- [ ] **Step 1: Başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Lcd/LcdMenuModelTests.cs`:
```csharp
using KontroXXL.Core.Lcd;
using Xunit;

namespace KontroXXL.Core.Tests.Lcd;

public class LcdMenuModelTests
{
    static readonly MenuCounts Counts = new(Apps: 3, Pools: 2, Shortcuts: 4);
    static readonly MenuCounts Empty  = new(Apps: 0, Pools: 0, Shortcuts: 0);

    static LcdTransition Apply(LcdMenuState s, LcdInput i, MenuCounts? c = null)
        => LcdMenuModel.Apply(s, i, c ?? Counts);

    [Fact]
    public void Home_up_and_down_change_volume_without_changing_state()
    {
        var t = Apply(LcdMenuState.Initial, LcdInput.Up);
        Assert.Equal(LcdEffect.VolumeUp, t.Effect);
        Assert.Equal(LcdMenuState.Initial, t.State);

        Assert.Equal(LcdEffect.VolumeDown, Apply(LcdMenuState.Initial, LcdInput.Down).Effect);
    }

    [Fact]
    public void Home_back_cycles_through_four_pages()
    {
        var s = LcdMenuState.Initial;
        for (int expected = 1; expected <= 4; expected++)
        {
            s = Apply(s, LcdInput.Back).State;
            Assert.Equal(expected % 4, s.Page);
            Assert.Equal(LcdMode.Home, s.Mode);
        }
    }

    [Fact]
    public void Home_click_opens_menu_at_index_zero()
    {
        var s = Apply(new LcdMenuState(LcdMode.Home, 7, 2), LcdInput.Click).State;
        Assert.Equal(LcdMode.Menu, s.Mode);
        Assert.Equal(0, s.Index);
        Assert.Equal(2, s.Page); // sayfa hatırlanır
    }

    [Theory]
    [InlineData(0, LcdMode.Apps,      LcdEffect.RequestSync)]
    [InlineData(1, LcdMode.Pools,     LcdEffect.RequestSync)]
    [InlineData(2, LcdMode.Shortcuts, LcdEffect.None)]
    [InlineData(3, LcdMode.NasPower,  LcdEffect.None)]
    public void Menu_click_enters_the_selected_submode(int index, LcdMode mode, LcdEffect effect)
    {
        var t = Apply(new LcdMenuState(LcdMode.Menu, index, 0), LcdInput.Click);
        Assert.Equal(mode, t.State.Mode);
        Assert.Equal(0, t.State.Index);
        Assert.Equal(effect, t.Effect);
    }

    [Fact]
    public void Menu_index_wraps_in_both_directions()
    {
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Menu, 3, 0), LcdInput.Up).State.Index);
        Assert.Equal(3, Apply(new LcdMenuState(LcdMode.Menu, 0, 0), LcdInput.Down).State.Index);
    }

    [Fact]
    public void Apps_index_wraps_over_the_actual_list_length()
    {
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Apps, 2, 0), LcdInput.Up).State.Index);
        Assert.Equal(2, Apply(new LcdMenuState(LcdMode.Apps, 0, 0), LcdInput.Down).State.Index);
    }

    [Fact]
    public void Index_is_clamped_to_zero_when_the_list_is_empty()
    {
        // v2 hatası (A7): liste boşken index bırakılıyordu, formatter appsList[index] ile patlıyordu.
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Apps, 5, 0), LcdInput.Up, Empty).State.Index);
        Assert.Equal(0, Apply(new LcdMenuState(LcdMode.Apps, 5, 0), LcdInput.Down, Empty).State.Index);
    }

    [Fact]
    public void Index_is_clamped_when_the_list_shrank_since_the_last_input()
    {
        // 5. uygulamada duruyorduk, liste 3'e düştü.
        var t = Apply(new LcdMenuState(LcdMode.Apps, 5, 0), LcdInput.Click);
        Assert.Equal(LcdEffect.ToggleApp, t.Effect);
        Assert.InRange(t.EffectIndex, 0, 2);
    }

    [Fact]
    public void Apps_click_toggles_the_selected_app()
    {
        var t = Apply(new LcdMenuState(LcdMode.Apps, 1, 0), LcdInput.Click);
        Assert.Equal(LcdEffect.ToggleApp, t.Effect);
        Assert.Equal(1, t.EffectIndex);
        Assert.Equal(LcdMode.Apps, t.State.Mode); // listede kalır
    }

    [Fact]
    public void Apps_click_does_nothing_when_the_list_is_empty()
        => Assert.Equal(LcdEffect.None, Apply(new LcdMenuState(LcdMode.Apps, 0, 0), LcdInput.Click, Empty).Effect);

    [Fact]
    public void Pools_click_does_nothing()
        => Assert.Equal(LcdEffect.None, Apply(new LcdMenuState(LcdMode.Pools, 1, 0), LcdInput.Click).Effect);

    [Fact]
    public void Shortcut_click_runs_it_and_returns_home()
    {
        var t = Apply(new LcdMenuState(LcdMode.Shortcuts, 2, 0), LcdInput.Click);
        Assert.Equal(LcdEffect.RunShortcut, t.Effect);
        Assert.Equal(2, t.EffectIndex);
        Assert.Equal(LcdMode.Home, t.State.Mode);
    }

    [Theory]
    [InlineData(0, LcdEffect.NasReboot)]
    [InlineData(1, LcdEffect.NasShutdown)]
    [InlineData(2, LcdEffect.None)]
    public void NasPower_click_fires_the_right_action_and_returns_home(int index, LcdEffect effect)
    {
        var t = Apply(new LcdMenuState(LcdMode.NasPower, index, 0), LcdInput.Click);
        Assert.Equal(effect, t.Effect);
        Assert.Equal(LcdMode.Home, t.State.Mode);
    }

    [Theory]
    [InlineData(LcdMode.Menu)]
    [InlineData(LcdMode.Apps)]
    [InlineData(LcdMode.Pools)]
    [InlineData(LcdMode.Shortcuts)]
    [InlineData(LcdMode.NasPower)]
    public void Back_from_any_submode_returns_home(LcdMode mode)
    {
        var t = Apply(new LcdMenuState(mode, 2, 1), LcdInput.Back);
        Assert.Equal(LcdMode.Home, t.State.Mode);
        Assert.Equal(0, t.State.Index);
        Assert.Equal(LcdEffect.None, t.Effect);
    }

    [Fact]
    public void Apply_never_returns_a_negative_or_out_of_range_index()
    {
        var rng = new Random(1234);
        var modes = Enum.GetValues<LcdMode>();
        var inputs = Enum.GetValues<LcdInput>();
        var s = LcdMenuState.Initial;

        for (int i = 0; i < 5000; i++)
        {
            var counts = new MenuCounts(rng.Next(0, 6), rng.Next(0, 6), rng.Next(0, 6));
            s = LcdMenuModel.Apply(s with { Mode = modes[rng.Next(modes.Length)] },
                                   inputs[rng.Next(inputs.Length)], counts).State;
            Assert.True(s.Index >= 0, $"negatif index: {s}");
            Assert.InRange(s.Page, 0, 3);
        }
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter LcdMenuModelTests`
Expected: FAIL — `LcdMenuModel` bulunamıyor (CS0246).

- [ ] **Step 3: `LcdMenuModel`'i yaz**

`src/KontroXXL.Core/Lcd/LcdMenuModel.cs`:
```csharp
namespace KontroXXL.Core.Lcd;

public enum LcdMode { Home, Menu, Apps, Pools, Shortcuts, NasPower }

public enum LcdInput { Up, Down, Click, Back }

/// <summary>Durum makinesinin çağırandan yapmasını istediği tek yan etki.</summary>
public enum LcdEffect
{
    None, VolumeUp, VolumeDown, RequestSync,
    ToggleApp, RunShortcut, NasReboot, NasShutdown,
}

/// <summary>Girdi anındaki liste uzunlukları. Model bunun dışında hiçbir dünya bilgisine sahip değildir.</summary>
public readonly record struct MenuCounts(int Apps, int Pools, int Shortcuts);

public sealed record LcdMenuState(LcdMode Mode, int Index, int Page)
{
    public static readonly LcdMenuState Initial = new(LcdMode.Home, 0, 0);
}

public readonly record struct LcdTransition(LcdMenuState State, LcdEffect Effect, int EffectIndex);

/// <summary>
/// LCD menüsünün tamamı. Saf fonksiyon: aynı girdi her zaman aynı çıktıyı verir,
/// zamana veya paylaşılan duruma dokunmaz. v2'de bu mantık iki thread'den kilitsiz
/// erişilen alanlara dağılmıştı (A7).
/// </summary>
public static class LcdMenuModel
{
    public const int MenuItemCount = 4;      // NAS APPS / NAS POOLS / SHORTCUTS / NAS POWER
    public const int NasPowerItemCount = 3;  // REBOOT / SHUTDOWN / CANCEL
    public const int HomePageCount = 4;      // CPU / GPU / NAS / ALERTS

    public static LcdTransition Apply(LcdMenuState state, LcdInput input, MenuCounts counts)
    {
        int max = MaxFor(state.Mode, counts);
        int index = Clamp(state.Index, max);
        var s = state with { Index = index };

        return input switch
        {
            LcdInput.Back => Back(s),
            LcdInput.Up => Step(s, +1, max),
            LcdInput.Down => Step(s, -1, max),
            LcdInput.Click => Click(s, max),
            _ => new LcdTransition(s, LcdEffect.None, 0),
        };
    }

    static int MaxFor(LcdMode mode, MenuCounts c) => mode switch
    {
        LcdMode.Menu => MenuItemCount,
        LcdMode.Apps => c.Apps,
        LcdMode.Pools => c.Pools,
        LcdMode.Shortcuts => c.Shortcuts,
        LcdMode.NasPower => NasPowerItemCount,
        _ => 0,
    };

    static int Clamp(int index, int max)
    {
        if (max <= 0) return 0;
        if (index < 0) return 0;
        return index >= max ? max - 1 : index;
    }

    static LcdTransition Back(LcdMenuState s) =>
        s.Mode == LcdMode.Home
            ? new LcdTransition(s with { Page = (s.Page + 1) % HomePageCount }, LcdEffect.None, 0)
            : new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, LcdEffect.None, 0);

    static LcdTransition Step(LcdMenuState s, int delta, int max)
    {
        if (s.Mode == LcdMode.Home)
            return new LcdTransition(s, delta > 0 ? LcdEffect.VolumeUp : LcdEffect.VolumeDown, 0);

        if (max <= 0)
            return new LcdTransition(s with { Index = 0 }, LcdEffect.None, 0);

        int next = ((s.Index + delta) % max + max) % max;
        return new LcdTransition(s with { Index = next }, LcdEffect.None, 0);
    }

    static LcdTransition Click(LcdMenuState s, int max)
    {
        switch (s.Mode)
        {
            case LcdMode.Home:
                return new LcdTransition(s with { Mode = LcdMode.Menu, Index = 0 }, LcdEffect.None, 0);

            case LcdMode.Menu:
                return s.Index switch
                {
                    0 => new LcdTransition(s with { Mode = LcdMode.Apps, Index = 0 }, LcdEffect.RequestSync, 0),
                    1 => new LcdTransition(s with { Mode = LcdMode.Pools, Index = 0 }, LcdEffect.RequestSync, 0),
                    2 => new LcdTransition(s with { Mode = LcdMode.Shortcuts, Index = 0 }, LcdEffect.None, 0),
                    _ => new LcdTransition(s with { Mode = LcdMode.NasPower, Index = 0 }, LcdEffect.None, 0),
                };

            case LcdMode.Apps:
                return max <= 0
                    ? new LcdTransition(s with { Index = 0 }, LcdEffect.None, 0)
                    : new LcdTransition(s, LcdEffect.ToggleApp, s.Index);

            case LcdMode.Shortcuts:
                return max <= 0
                    ? new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, LcdEffect.None, 0)
                    : new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, LcdEffect.RunShortcut, s.Index);

            case LcdMode.NasPower:
                var effect = s.Index switch
                {
                    0 => LcdEffect.NasReboot,
                    1 => LcdEffect.NasShutdown,
                    _ => LcdEffect.None,
                };
                return new LcdTransition(s with { Mode = LcdMode.Home, Index = 0 }, effect, 0);

            default: // Pools — v2'de de tıklamanın etkisi yok
                return new LcdTransition(s, LcdEffect.None, 0);
        }
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter LcdMenuModelTests`
Expected: PASS — 20 test (Theory'ler dahil).

- [ ] **Step 5: Commit**

```bash
git add src/KontroXXL.Core/Lcd/LcdMenuModel.cs tests/KontroXXL.Core.Tests/Lcd/LcdMenuModelTests.cs
git commit -m "feat(lcd): pure menu state machine with index clamping (A7)"
```

---

## Task 5: `LcdFormatter` — çerçeve üretimi

**Files:**
- Create: `src/KontroXXL.Core/Lcd/LcdFrame.cs`
- Create: `src/KontroXXL.Core/Lcd/LcdViewData.cs`
- Create: `src/KontroXXL.Core/Lcd/LcdFormatter.cs`
- Test: `tests/KontroXXL.Core.Tests/Lcd/LcdFormatterTests.cs`

**Interfaces:**
- Consumes: `LcdMenuState`, `LcdMode` (Task 4); `LcdText` (Task 2)
- Produces:
  - `sealed record LcdFrame(string Line0, string Line1, int? BarValue)`
  - `sealed record LcdViewData(...)` — aşağıdaki tam imza
  - `sealed record LcdRenderContext(DateTime Now, int ScrollOffset, bool VolumeActive, int VolumePercent, string? TickerText, int TickerOffset)`
  - `static LcdFrame LcdFormatter.Render(LcdMenuState state, LcdViewData data, LcdRenderContext ctx)`

  `Render` **saftır**: `DateTime.Now`'a dokunmaz, kaydırma sayaçlarını kendisi artırmaz. Zaman ve sayaçlar `ctx` ile içeri verilir — v2'de bunlar metodun içindeydi ve test edilemiyordu.

- [ ] **Step 1: Başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Lcd/LcdFormatterTests.cs`:
```csharp
using KontroXXL.Core.Lcd;
using Xunit;

namespace KontroXXL.Core.Tests.Lcd;

public class LcdFormatterTests
{
    static readonly DateTime Now = new(2026, 9, 1, 4, 35, 0);

    static LcdViewData Data(bool nasOnline = true) => new(
        Cpu: 76, CpuGhz: 3.6, Ram: 42,
        Gpu: 45, GpuTemp: 68, GpuFan: 30, NetMbps: 7,
        NasCpu: 55, NasTemp: 42, NasRx: 3, NasTx: 0, NasAlerts: 2, NasOnline: nasOnline,
        AppNames: new[] { "plex", "sonarr" }, AppRunning: new[] { true, false },
        PoolNames: new[] { "NasServer", "ssd-app" }, PoolUsed: new[] { 67, 68 },
        ShortcutNames: new[] { "Obsidian", "Müzik Çalar" });

    static LcdRenderContext Ctx(bool volume = false, int volumePct = 0,
                                string? ticker = null, int scroll = 0) =>
        new(Now, ScrollOffset: scroll, VolumeActive: volume, VolumePercent: volumePct,
            TickerText: ticker, TickerOffset: 0);

    // --- EN ÖNEMLİ TEST: 16 karakter invaryantı ---

    public static IEnumerable<object[]> AllStates()
    {
        foreach (var mode in Enum.GetValues<LcdMode>())
            for (int page = 0; page < 4; page++)
                for (int index = 0; index < 3; index++)
                    yield return new object[] { new LcdMenuState(mode, index, page) };
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public void Every_frame_is_exactly_16_characters(LcdMenuState state)
    {
        foreach (var data in new[] { Data(true), Data(false) })
        foreach (var ctx in new[] { Ctx(), Ctx(volume: true, volumePct: 55), Ctx(ticker: "! YENI ALARM !") })
        {
            var f = LcdFormatter.Render(state, data, ctx);
            Assert.Equal(16, f.Line0.Length);
            Assert.Equal(16, f.Line1.Length);
        }
    }

    [Fact]
    public void Empty_lists_do_not_throw_and_still_produce_16_chars()
    {
        var empty = Data() with
        {
            AppNames = Array.Empty<string>(), AppRunning = Array.Empty<bool>(),
            PoolNames = Array.Empty<string>(), PoolUsed = Array.Empty<int>(),
            ShortcutNames = Array.Empty<string>(),
        };

        foreach (var mode in new[] { LcdMode.Apps, LcdMode.Pools, LcdMode.Shortcuts })
        {
            var f = LcdFormatter.Render(new LcdMenuState(mode, 0, 0), empty, Ctx());
            Assert.Equal(16, f.Line0.Length);
            Assert.Equal(16, f.Line1.Length);
        }
    }

    // --- Sayfa içerikleri (v2 davranışıyla birebir) ---

    [Fact]
    public void Home_page0_shows_cpu_left_and_frequency_right()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 0), Data(), Ctx());
        Assert.Equal("CPU:76%    3.60G", f.Line0);
        Assert.Equal("RAM:42%    04:35", f.Line1);
    }

    [Fact]
    public void Home_page0_still_fits_at_100_percent()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 0),
                                    Data() with { Cpu = 100, Ram = 100 }, Ctx());
        Assert.Equal("CPU:100%   3.60G", f.Line0);
        Assert.Equal(16, f.Line1.Length);
    }

    [Fact]
    public void Home_page1_shows_gpu_and_network()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 1), Data(), Ctx());
        Assert.Equal("GPU:45% 68C     ", f.Line0);
        Assert.Equal("Fan:30% 7Mbps   ", f.Line1);
    }

    [Fact]
    public void Home_page2_shows_offline_banner_when_nas_is_down()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 2), Data(nasOnline: false), Ctx());
        Assert.Equal("  NAS: OFFLINE  ", f.Line0);
        Assert.Equal(" No Connection  ", f.Line1);
    }

    [Fact]
    public void Home_page2_uses_the_custom_arrow_bytes_for_rx_and_tx()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 2), Data(), Ctx());
        Assert.Equal("NAS:55% 42C     ", f.Line0);
        Assert.StartsWith("\x01", f.Line1);
        Assert.Contains("\x02", f.Line1);
    }

    [Fact]
    public void Home_page3_shows_alert_count()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 3), Data(), Ctx());
        Assert.Equal("> NAS DASHBOARD ", f.Line0);
        Assert.Equal("2 SYSTEM ALERTS!", f.Line1);
    }

    [Fact]
    public void Home_page3_shows_the_calm_message_at_zero_alerts()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 3), Data() with { NasAlerts = 0 }, Ctx());
        Assert.Equal("No active alerts", f.Line1);
    }

    // --- Kaplamalar ---

    [Fact]
    public void Volume_overlay_takes_over_home_and_sets_the_bar()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 1), Data(), Ctx(volume: true, volumePct: 55));
        Assert.Equal(" SYSTEM VOLUME  ", f.Line0);
        Assert.Equal(55, f.BarValue);
    }

    [Fact]
    public void Ticker_overrides_line0_except_on_the_alert_page()
    {
        var withTicker = Ctx(ticker: "! YENI ALARM: 2 uyari aktif !  ");

        var onPage0 = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 0), Data(), withTicker);
        Assert.StartsWith("! YENI", onPage0.Line0);

        var onPage3 = LcdFormatter.Render(new LcdMenuState(LcdMode.Home, 0, 3), Data(), withTicker);
        Assert.Equal("> NAS DASHBOARD ", onPage3.Line0);
    }

    // --- Alt modlar ---

    [Fact]
    public void Menu_lists_the_four_entries()
    {
        Assert.Equal("1. NAS APPS     ", LcdFormatter.Render(new LcdMenuState(LcdMode.Menu, 0, 0), Data(), Ctx()).Line1);
        Assert.Equal("4. NAS POWER    ", LcdFormatter.Render(new LcdMenuState(LcdMode.Menu, 3, 0), Data(), Ctx()).Line1);
    }

    [Fact]
    public void Apps_shows_the_name_and_running_state()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 0, 0), Data(), Ctx());
        Assert.Equal("plex            ", f.Line0);
        Assert.Equal(">> RUNNING <<   ", f.Line1);

        var stopped = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 1, 0), Data(), Ctx());
        Assert.Equal(">> STOPPED <<   ", stopped.Line1);
    }

    [Fact]
    public void Pools_puts_the_usage_on_the_bar()
    {
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Pools, 1, 0), Data(), Ctx());
        Assert.Equal("ssd-app         ", f.Line0);
        Assert.Equal(68, f.BarValue);
    }

    [Fact]
    public void Shortcut_names_are_transliterated_before_reaching_the_lcd()
    {
        // v2 hatası (A10): "Müzik Çalar" ekrana "M?zik ?alar" olarak gidiyordu.
        var f = LcdFormatter.Render(new LcdMenuState(LcdMode.Shortcuts, 1, 0), Data(), Ctx());
        Assert.Equal("ACTIONS:        ", f.Line0);
        Assert.Equal("Muzik Calar     ", f.Line1);
    }

    [Fact]
    public void Nas_power_lists_its_three_entries()
    {
        Assert.Equal("1. NAS REBOOT   ", LcdFormatter.Render(new LcdMenuState(LcdMode.NasPower, 0, 0), Data(), Ctx()).Line1);
        Assert.Equal("2. NAS SHUTDOWN ", LcdFormatter.Render(new LcdMenuState(LcdMode.NasPower, 1, 0), Data(), Ctx()).Line1);
        Assert.Equal("3. CANCEL       ", LcdFormatter.Render(new LcdMenuState(LcdMode.NasPower, 2, 0), Data(), Ctx()).Line1);
    }

    [Fact]
    public void Long_names_scroll_with_the_supplied_offset()
    {
        var data = Data() with { AppNames = new[] { "cok-uzun-uygulama-adi-buraya" }, AppRunning = new[] { true } };
        var a = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 0, 0), data, Ctx(scroll: 0)).Line0;
        var b = LcdFormatter.Render(new LcdMenuState(LcdMode.Apps, 0, 0), data, Ctx(scroll: 4)).Line0;
        Assert.NotEqual(a, b);
        Assert.Equal(16, b.Length);
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter LcdFormatterTests`
Expected: FAIL — `LcdFormatter`, `LcdViewData`, `LcdFrame`, `LcdRenderContext` bulunamıyor.

- [ ] **Step 3: Tipleri ve formatter'ı yaz**

`src/KontroXXL.Core/Lcd/LcdFrame.cs`:
```csharp
namespace KontroXXL.Core.Lcd;

/// <summary>
/// Ekrana gidecek tek kare. <see cref="BarValue"/> doluysa çağıran L1 yerine B1 gönderir.
/// Her iki satır da her zaman tam 16 karakterdir.
/// </summary>
public sealed record LcdFrame(string Line0, string Line1, int? BarValue);
```

`src/KontroXXL.Core/Lcd/LcdViewData.cs`:
```csharp
namespace KontroXXL.Core.Lcd;

/// <summary>Formatter'ın ihtiyaç duyduğu dünyanın anlık görüntüsü. Değişmez.</summary>
public sealed record LcdViewData(
    int Cpu, double CpuGhz, int Ram,
    int Gpu, int GpuTemp, int GpuFan, double NetMbps,
    int NasCpu, int NasTemp, double NasRx, double NasTx, int NasAlerts, bool NasOnline,
    IReadOnlyList<string> AppNames, IReadOnlyList<bool> AppRunning,
    IReadOnlyList<string> PoolNames, IReadOnlyList<int> PoolUsed,
    IReadOnlyList<string> ShortcutNames)
{
    public MenuCounts Counts => new(AppNames.Count, PoolNames.Count, ShortcutNames.Count);
}

/// <summary>
/// Zamana bağlı her şey buradan içeri verilir; böylece Render saf ve deterministik kalır.
/// </summary>
public sealed record LcdRenderContext(
    DateTime Now, int ScrollOffset,
    bool VolumeActive, int VolumePercent,
    string? TickerText, int TickerOffset);
```

`src/KontroXXL.Core/Lcd/LcdFormatter.cs`:
```csharp
using System.Globalization;

namespace KontroXXL.Core.Lcd;

/// <summary>
/// Durum + veri -> 16x2 kare. Saf fonksiyon. Tüm çıktı LcdText.Fit'ten geçer,
/// dolayısıyla 16 karakter invaryantı yapısal olarak garanti altındadır.
/// </summary>
public static class LcdFormatter
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static LcdFrame Render(LcdMenuState state, LcdViewData d, LcdRenderContext ctx) => state.Mode switch
    {
        LcdMode.Home => RenderHome(state, d, ctx),
        LcdMode.Menu => new LcdFrame(LcdText.Fit("> SYSTEM MENU"), MenuItem(state.Index), null),
        LcdMode.Apps => RenderApps(state, d, ctx),
        LcdMode.Pools => RenderPools(state, d, ctx),
        LcdMode.Shortcuts => RenderShortcuts(state, d, ctx),
        LcdMode.NasPower => new LcdFrame(LcdText.Fit("> NAS POWER"), NasPowerItem(state.Index), null),
        _ => new LcdFrame(LcdText.Fit(""), LcdText.Fit(""), null),
    };

    static LcdFrame RenderHome(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (ctx.VolumeActive)
            return new LcdFrame(LcdText.Fit(" SYSTEM VOLUME"), LcdText.Fit(""), Clamp0To100(ctx.VolumePercent));

        (string l0, string l1) = s.Page switch
        {
            0 => (SplitLine($"CPU:{d.Cpu}%", d.CpuGhz.ToString("0.00", Inv) + "G"),
                  SplitLine($"RAM:{d.Ram}%", ctx.Now.ToString("HH:mm", Inv))),

            1 => ($"GPU:{d.Gpu}% {d.GpuTemp}C",
                  $"Fan:{d.GpuFan}% {(int)d.NetMbps}Mbps"),

            2 => d.NasOnline
                 ? ($"NAS:{d.NasCpu}% {d.NasTemp}C",
                    string.Format(Inv, "{0}{1,3}Mb {2}{3,3}Mb", LcdText.RxArrow, (int)d.NasRx, LcdText.TxArrow, (int)d.NasTx))
                 : ("  NAS: OFFLINE", " No Connection"),

            _ => ("> NAS DASHBOARD",
                  d.NasAlerts == 0 ? "No active alerts" : $"{d.NasAlerts} SYSTEM ALERTS!"),
        };

        // Ticker, alarm sayfası hariç üst satırı devralır.
        if (s.Page != 3 && !string.IsNullOrEmpty(ctx.TickerText))
            l0 = LcdText.Scroll(ctx.TickerText, ctx.TickerOffset);

        return new LcdFrame(LcdText.Fit(l0), LcdText.Fit(l1), null);
    }

    static LcdFrame RenderApps(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (d.AppNames.Count == 0)
            return new LcdFrame(LcdText.Fit(" APPLICATIONS"), LcdText.Fit(" Syncing..."), null);

        int i = SafeIndex(s.Index, d.AppNames.Count);
        bool running = i < d.AppRunning.Count && d.AppRunning[i];
        return new LcdFrame(
            LcdText.Scroll(d.AppNames[i], ctx.ScrollOffset),
            LcdText.Fit(running ? ">> RUNNING <<" : ">> STOPPED <<"),
            null);
    }

    static LcdFrame RenderPools(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (d.PoolNames.Count == 0)
            return new LcdFrame(LcdText.Fit(" STORAGE POOLS"), LcdText.Fit(" Syncing..."), null);

        int i = SafeIndex(s.Index, d.PoolNames.Count);
        int used = i < d.PoolUsed.Count ? d.PoolUsed[i] : 0;
        return new LcdFrame(LcdText.Scroll(d.PoolNames[i], ctx.ScrollOffset), LcdText.Fit(""), Clamp0To100(used));
    }

    static LcdFrame RenderShortcuts(LcdMenuState s, LcdViewData d, LcdRenderContext ctx)
    {
        if (d.ShortcutNames.Count == 0)
            return new LcdFrame(LcdText.Fit("ACTIONS:"), LcdText.Fit(" No Shortcuts"), null);

        int i = SafeIndex(s.Index, d.ShortcutNames.Count);
        return new LcdFrame(LcdText.Fit("ACTIONS:"), LcdText.Scroll(d.ShortcutNames[i], ctx.ScrollOffset), null);
    }

    static string MenuItem(int index) => LcdText.Fit(index switch
    {
        0 => "1. NAS APPS",
        1 => "2. NAS POOLS",
        2 => "3. SHORTCUTS",
        _ => "4. NAS POWER",
    });

    static string NasPowerItem(int index) => LcdText.Fit(index switch
    {
        0 => "1. NAS REBOOT",
        1 => "2. NAS SHUTDOWN",
        _ => "3. CANCEL",
    });

    /// <summary>Sol kısım sola, sağ kısım sağa yaslı; toplam 16 karakter.</summary>
    static string SplitLine(string left, string right)
    {
        int gap = LcdText.Width - left.Length - right.Length;
        return gap > 0 ? left + new string(' ', gap) + right : left + right;
    }

    static int SafeIndex(int index, int count) => count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);
    static int Clamp0To100(int v) => Math.Clamp(v, 0, 100);
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter LcdFormatterTests`
Expected: PASS — 90'ın üzerinde test vakası (72'si 16 karakter Theory'sinden).

- [ ] **Step 5: Tüm test paketini koştur**

Run: `dotnet test`
Expected: PASS — tümü yeşil.

- [ ] **Step 6: Commit**

```bash
git add src/KontroXXL.Core/Lcd tests/KontroXXL.Core.Tests/Lcd
git commit -m "feat(lcd): pure frame formatter enforcing the 16-char invariant"
```

---

## Task 6: `TrayApplicationContext`'i Core LCD çekirdeğine bağla

**Files:**
- Modify: `src/KontroXXL_WinApp/TrayApplicationContext.cs:33-70,318-502`

**Interfaces:**
- Consumes: `LcdMenuModel.Apply` (Task 4), `LcdFormatter.Render` (Task 5), `LcdViewData`/`LcdRenderContext` (Task 5)
- Produces: `private LcdViewData BuildViewData()` — sonraki görevler (ve Faz 3) bu metodu taşır.

- [ ] **Step 1: Eski LCD durum alanlarını sil**

`TrayApplicationContext.cs:33-38` arasındaki şu satırları **kaldır**:
```csharp
        private enum LcdMode { Home, Menu, Apps, Pools, Shortcuts, NasPower }
        private LcdMode currentLcdMode = LcdMode.Home;
        private int lcdIndex = 0;
        private int lcdPage = 0;
        private int scrollIdx = 0;
        private DateTime lastScrollUpdate = DateTime.Now;
```

Yerine:
```csharp
        // LCD durumu tek bir referansta toplandı; yalnızca UI thread'inden değiştirilir (A7).
        private LcdMenuState lcdState = LcdMenuState.Initial;
        private int scrollOffset = 0;
        private DateTime lastScrollTick = DateTime.Now;
```

Dosyanın başına ekle: `using KontroXXL.Core.Lcd;`

- [ ] **Step 2: `HandleArduinoEvent` ve `FixIndex`'i durum makinesine devret**

`318-381` arasındaki `HandleArduinoEvent` ve `FixIndex` metodlarını tamamen şununla değiştir:

```csharp
        private void HandleArduinoEvent(string ev)
        {
            LcdInput input;
            switch (ev)
            {
                case "UP":    input = LcdInput.Up; break;
                case "DN":    input = LcdInput.Down; break;
                case "CLICK": input = LcdInput.Click; break;
                case "BACK":  input = LcdInput.Back; break;
                default: return;
            }

            // Seri thread'inden geliyoruz; durum değişimini UI thread'ine sıraya al (A7).
            if (mainForm != null && mainForm.IsHandleCreated && !mainForm.IsDisposed)
                mainForm.BeginInvoke((MethodInvoker)(() => ApplyInput(input)));
            else
                ApplyInput(input);
        }

        private void ApplyInput(LcdInput input)
        {
            var data = BuildViewData();
            var previous = lcdState;
            var transition = LcdMenuModel.Apply(previous, input, data.Counts);
            lcdState = transition.State;

            // Mod veya sayfa değiştiyse ekranı temizle ve tam yeniden çizime zorla.
            if (lcdState.Mode != previous.Mode || lcdState.Page != previous.Page)
            {
                SendData("CLR");
                lastL0 = ""; lastL1 = "";
                scrollOffset = 0;
            }

            RunEffect(transition.Effect, transition.EffectIndex);
            UpdateLCD(forced: true);
        }

        private void RunEffect(LcdEffect effect, int index)
        {
            switch (effect)
            {
                case LcdEffect.VolumeUp:   NudgeVolume(+2); break;
                case LcdEffect.VolumeDown: NudgeVolume(-2); break;
                case LcdEffect.RequestSync: _ = PushArduinoData(); break;
                case LcdEffect.ToggleApp:  ToggleApp(index); break;
                case LcdEffect.RunShortcut: RunShortcut(index); break;
                case LcdEffect.NasReboot:   _ = NasPower("REBOOT"); break;
                case LcdEffect.NasShutdown: _ = NasPower("SHUTDOWN"); break;
            }
        }

        private void NudgeVolume(int delta)
        {
            try
            {
                var dev = audioController?.DefaultPlaybackDevice;
                if (dev == null) return;
                dev.Volume = Math.Max(0, Math.Min(100, dev.Volume + delta));
                volumeShowUntil = DateTime.Now.AddSeconds(2);
            }
            catch (Exception ex) { log.Debug("Ses ayarlanamadi: " + ex.Message); }
        }

        private async Task NasPower(string action)
        {
            try
            {
                string endpoint = action == "REBOOT" ? "system/reboot" : "system/shutdown";
                log.Info($"NAS {action} komutu tetiklendi.");
                await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/{endpoint}", null);
            }
            catch (Exception ex) { log.Error($"NAS {action} hatasi", ex); }
        }
```

`HandleNasPower` metodunu (`540-554`) **sil** — `NasPower` onun yerini aldı.
`WireFormEvents` içindeki `form.OnNasPower` aboneliğini (`171-177`) şununla değiştir:
```csharp
            form.OnNasPower += (action) => _ = NasPower(action);
```

`RunShortcut` (`530-538`) içindeki son satır artık var olmayan alana yazıyor — **sil**:
```csharp
                currentLcdMode = LcdMode.Home;   // ← BU SATIRI SİL
```
Ana ekrana dönüşü artık `LcdMenuModel.Click` yapıyor (Task 4, `Shortcuts` dalı), metodun kendisi değil. Metot şu hâle gelir:
```csharp
        private void RunShortcut(int idx)
        {
            if (idx < 0 || idx >= config.Shortcuts.Count) return;
            try {
                Process.Start(new ProcessStartInfo(config.Shortcuts[idx].Path) {
                    UseShellExecute = true, Arguments = config.Shortcuts[idx].Arguments });
            }
            catch (Exception ex) { log.Error("Kisayol calistirilamadi: " + config.Shortcuts[idx].Name, ex); }
        }
```

- [ ] **Step 3: `BuildViewData` ekle**

Sınıfa ekle:
```csharp
        /// <summary>Formatter ve durum makinesi için dünyanın anlık görüntüsü.</summary>
        private LcdViewData BuildViewData()
        {
            var apps = appsList;   // yerel kopya — arada değişse bile tutarlı okuruz
            var pools = poolsList;

            var appNames = new List<string>(apps.Count);
            var appRunning = new List<bool>(apps.Count);
            foreach (var a in apps)
            {
                appNames.Add(a["name"]?.ToString() ?? "");
                string st = a["state"]?.ToString();
                appRunning.Add(st == "RUNNING" || st == "ACTIVE");
            }

            var poolNames = new List<string>(pools.Count);
            var poolUsed = new List<int>(pools.Count);
            foreach (var p in pools)
            {
                poolNames.Add(p["name"]?.ToString() ?? "");
                poolUsed.Add((int)(p["used"] ?? 0));
            }

            var shortcutNames = new List<string>(config.Shortcuts.Count);
            foreach (var s in config.Shortcuts) shortcutNames.Add(s.Name ?? "");

            return new LcdViewData(
                config.LastCpu, config.LastCpuFreq, config.LastRam,
                config.LastGpu, config.LastGpuTemp, config.LastGpuFan, config.LastNetSpeed,
                config.LastNasCpu, config.LastNasTemp, config.LastNasRx, config.LastNasTx,
                config.LastNasAlerts, nasLastConn,
                appNames, appRunning, poolNames, poolUsed, shortcutNames);
        }
```

- [ ] **Step 4: `UpdateLCD`'yi formatter'a devret**

`383-476` arasındaki `UpdateLCD` gövdesini ve `478-502` arasındaki `GetScrolled`/`GetTicker` metodlarını şununla değiştir:

```csharp
        private void UpdateLCD(bool forced = false)
        {
            if (serialPort == null || !serialPort.IsOpen) return;

            var now = DateTime.Now;
            if (forced && (now - lastLcdUpdate).TotalMilliseconds < 30) return;
            if (!forced && (now - lastLcdUpdate).TotalMilliseconds < 100) return;
            lastLcdUpdate = now;

            // Kaydırma sayaçları burada ilerler — formatter saf kalır.
            if ((now - lastScrollTick).TotalMilliseconds > 400) { scrollOffset++; lastScrollTick = now; }
            if (now >= _lcdTickerUntil) _lcdTickerText = "";
            else if ((now - _lastTickerScroll).TotalMilliseconds > 300) { _tickerScrollIdx++; _lastTickerScroll = now; }

            try
            {
                var ctx = new LcdRenderContext(
                    Now: now,
                    ScrollOffset: scrollOffset,
                    VolumeActive: now < volumeShowUntil,
                    VolumePercent: CurrentVolume(),
                    TickerText: string.IsNullOrEmpty(_lcdTickerText) ? null : _lcdTickerText,
                    TickerOffset: _tickerScrollIdx);

                var frame = LcdFormatter.Render(lcdState, BuildViewData(), ctx);

                if (forced || frame.Line0 != lastL0) { SendData("L0=" + frame.Line0); lastL0 = frame.Line0; }

                if (frame.BarValue.HasValue)
                {
                    string key = "BAR_" + frame.BarValue.Value;
                    if (forced || key != lastL1) { SendData("B1=" + frame.BarValue.Value); lastL1 = key; }
                }
                else if (forced || frame.Line1 != lastL1)
                {
                    SendData("L1=" + frame.Line1); lastL1 = frame.Line1;
                }
            }
            catch (Exception ex) { log.Error("UpdateLCD hatasi", ex); }
        }

        private int CurrentVolume()
        {
            try { return (int)(audioController?.DefaultPlaybackDevice?.Volume ?? 0); }
            catch { return 0; }
        }
```

- [ ] **Step 5: Derle**

Run: `dotnet build KontroXXL.sln -c Debug`
Expected: hata yok. Hata varsa büyük ihtimalle `lcdPage`/`currentLcdMode`/`lcdIndex`'e kalan bir referanstır — `lcdState.Page` / `lcdState.Mode` / `lcdState.Index` ile değiştir.

- [ ] **Step 6: Donanımla elle doğrula**

Uygulamayı çalıştır (`dotnet run --project src/KontroXXL_WinApp`) ve Arduino bağlıyken şu listeyi geç:
1. Encoder çevir → ses barı çıkıyor, 2 sn sonra ana sayfaya dönüyor.
2. GERİ tuşu 4 kez → dört ana sayfa sırayla geliyor, aralarda çöp karakter yok.
3. Tıkla → SYSTEM MENU. Encoder → 4 madde dönüyor.
4. NAS APPS → liste geliyor, tıkla → uygulama başlıyor/duruyor.
5. SHORTCUTS → **Türkçe karakterli bir kısayol ekleyip** listede `?` olmadan göründüğünü doğrula.
6. NAS POWER → CANCEL seç, hiçbir şey olmuyor.

- [ ] **Step 7: Commit**

```bash
git add src/KontroXXL_WinApp/TrayApplicationContext.cs
git commit -m "refactor(lcd): drive the LCD from the tested Core state machine and formatter (A7, A10)"
```

---

## Task 7: `SerialLineBuffer` + `SerialLink` — otomatik yeniden bağlanma (A2)

**Files:**
- Create: `src/KontroXXL.Core/Serial/SerialLineBuffer.cs`
- Create: `src/KontroXXL_WinApp/SerialLink.cs`
- Test: `tests/KontroXXL.Core.Tests/Serial/SerialLineBufferTests.cs`
- Modify: `src/KontroXXL_WinApp/TrayApplicationContext.cs:232-283,504-519`

**Interfaces:**
- Consumes: `ILog` (Task 3)
- Produces:
  - `sealed class SerialLineBuffer` — `IEnumerable<string> Feed(ReadOnlySpan<byte> chunk)`, `void Reset()`
  - `sealed class SerialLink : IDisposable` — `event Action<string>? LineReceived`, `bool IsConnected`, `void Send(string msg)`, `void Start()`, `void Stop()`

- [ ] **Step 1: `SerialLineBuffer` için başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Serial/SerialLineBufferTests.cs`:
```csharp
using System.Text;
using KontroXXL.Core.Serial;
using Xunit;

namespace KontroXXL.Core.Tests.Serial;

public class SerialLineBufferTests
{
    static byte[] B(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Emits_a_complete_line()
        => Assert.Equal(new[] { "EV:UP" }, new SerialLineBuffer().Feed(B("EV:UP\n")).ToArray());

    [Fact]
    public void Joins_a_line_split_across_two_chunks()
    {
        var buf = new SerialLineBuffer();
        Assert.Empty(buf.Feed(B("EV:")).ToArray());
        Assert.Equal(new[] { "EV:UP" }, buf.Feed(B("UP\n")).ToArray());
    }

    [Fact]
    public void Emits_two_lines_from_one_chunk()
        => Assert.Equal(new[] { "EV:UP", "EV:DN" },
                        new SerialLineBuffer().Feed(B("EV:UP\nEV:DN\n")).ToArray());

    [Fact]
    public void Strips_carriage_returns()
        => Assert.Equal(new[] { "CMD:READY" }, new SerialLineBuffer().Feed(B("CMD:READY\r\n")).ToArray());

    [Fact]
    public void Skips_empty_lines()
        => Assert.Equal(new[] { "EV:UP" }, new SerialLineBuffer().Feed(B("\n\nEV:UP\n\n")).ToArray());

    [Fact]
    public void Drops_a_runaway_line_instead_of_growing_without_bound()
    {
        var buf = new SerialLineBuffer(maxLineLength: 32);
        Assert.Empty(buf.Feed(B(new string('x', 500))).ToArray());
        // Taşma sonrası kendini toparlar:
        Assert.Equal(new[] { "EV:UP" }, buf.Feed(B("\nEV:UP\n")).ToArray());
    }

    [Fact]
    public void Reset_discards_a_partial_line()
    {
        var buf = new SerialLineBuffer();
        buf.Feed(B("EV:"));
        buf.Reset();
        Assert.Equal(new[] { "UP" }, buf.Feed(B("UP\n")).ToArray());
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter SerialLineBufferTests`
Expected: FAIL — `SerialLineBuffer` bulunamıyor (CS0246).

- [ ] **Step 3: `SerialLineBuffer`'ı yaz**

`src/KontroXXL.Core/Serial/SerialLineBuffer.cs`:
```csharp
using System.Text;

namespace KontroXXL.Core.Serial;

/// <summary>
/// Bayt akışını satırlara böler. SerialPort.ReadLine() bloklar ve kopmada
/// beklenmedik istisnalar fırlatır; okuma döngüsü ham bayt okuyup bunu kullanır.
/// </summary>
public sealed class SerialLineBuffer
{
    readonly StringBuilder _sb = new();
    readonly int _maxLineLength;
    bool _overflowed;

    public SerialLineBuffer(int maxLineLength = 256) => _maxLineLength = maxLineLength;

    public IEnumerable<string> Feed(ReadOnlySpan<byte> chunk)
    {
        var lines = new List<string>();
        foreach (byte b in chunk)
        {
            char c = (char)b;
            if (c == '\n')
            {
                if (!_overflowed && _sb.Length > 0) lines.Add(_sb.ToString());
                _sb.Clear();
                _overflowed = false;
                continue;
            }
            if (c == '\r') continue;

            if (_sb.Length >= _maxLineLength) { _overflowed = true; _sb.Clear(); continue; }
            _sb.Append(c);
        }
        return lines;
    }

    public void Reset() { _sb.Clear(); _overflowed = false; }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter SerialLineBufferTests`
Expected: PASS — 7 test.

- [ ] **Step 5: `SerialLink`'i yaz**

`src/KontroXXL_WinApp/SerialLink.cs`:
```csharp
using System;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using KontroXXL.Core.Logging;
using KontroXXL.Core.Serial;

namespace KontroXXL_WinApp
{
    /// <summary>
    /// Seri bağlantıyı kendi başına ayakta tutar. v2'de (A2) port bir kez açılıyordu;
    /// Arduino çıkarılınca uygulama sessizce ölüyordu. Burada 2 saniyede bir yeniden dener.
    /// </summary>
    public sealed class SerialLink : IDisposable
    {
        const int ReconnectDelayMs = 2000;
        const int ReadBufferSize = 256;

        readonly ILog _log;
        readonly Func<string> _preferredPort;   // config'ten anlık okunur
        readonly Func<int> _baud;
        readonly Func<bool> _autoDetect;
        readonly SerialLineBuffer _lineBuffer = new();
        readonly object _writeGate = new();

        CancellationTokenSource _cts;
        SerialPort _port;
        Task _loop;

        public event Action<string> LineReceived;
        public event Action Connected;

        public bool IsConnected => _port != null && _port.IsOpen;
        public string CurrentPort { get; private set; }

        public SerialLink(ILog log, Func<string> preferredPort, Func<int> baud, Func<bool> autoDetect)
        {
            _log = log ?? NullLog.Instance;
            _preferredPort = preferredPort;
            _baud = baud;
            _autoDetect = autoDetect;
        }

        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _loop?.Wait(3000); } catch { }
            ClosePort();
            _loop = null;
        }

        public void Send(string msg)
        {
            lock (_writeGate)
            {
                var p = _port;
                if (p == null || !p.IsOpen) return;
                try { p.Write(msg + "\n"); }
                catch (Exception ex) { _log.Debug("Seri yazma hatasi: " + ex.Message); ClosePort(); }
            }
        }

        async Task RunAsync(CancellationToken ct)
        {
            var buffer = new byte[ReadBufferSize];

            while (!ct.IsCancellationRequested)
            {
                if (!IsConnected)
                {
                    if (!TryOpen()) { await Delay(ReconnectDelayMs, ct); continue; }
                    Connected?.Invoke();
                }

                try
                {
                    int n = await _port.BaseStream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (n <= 0) { ClosePort(); continue; }

                    foreach (string line in _lineBuffer.Feed(buffer.AsSpan(0, n)))
                    {
                        try { LineReceived?.Invoke(line); }
                        catch (Exception ex) { _log.Error("Seri satir isleme hatasi", ex); }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.Info("Seri baglanti koptu: " + ex.Message);
                    ClosePort();
                    await Delay(ReconnectDelayMs, ct);
                }
            }

            ClosePort();
        }

        static async Task Delay(int ms, CancellationToken ct)
        {
            try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
        }

        bool TryOpen()
        {
            string target = ResolvePort();
            if (string.IsNullOrEmpty(target)) return false;

            try
            {
                var p = new SerialPort(target, _baud()) { DtrEnable = true, RtsEnable = true };
                p.Open();
                _port = p;
                CurrentPort = target;
                _lineBuffer.Reset();
                _log.Info($"Seri port acildi: {target} @ {_baud()} baud");
                return true;
            }
            catch (Exception ex)
            {
                _log.Debug($"Seri port acilamadi ({target}): {ex.Message}");
                return false;
            }
        }

        string ResolvePort()
        {
            string preferred = _preferredPort();
            var available = SafePortNames();

            if (!_autoDetect() && !string.IsNullOrEmpty(preferred) && available.Contains(preferred))
                return preferred;

            string detected = DetectArduinoPort(available);
            if (!string.IsNullOrEmpty(detected)) return detected;

            // Otomatik algılama tutmadıysa tercih edilen porta yine de bir şans ver.
            return available.Contains(preferred) ? preferred : null;
        }

        static string[] SafePortNames()
        {
            try { return SerialPort.GetPortNames(); } catch { return Array.Empty<string>(); }
        }

        /// <summary>WMI ile Arduino/CH340/CP210x cihazının COM adını bulur.</summary>
        public static string DetectArduinoPort(string[] available)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                           "SELECT Caption FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'"))
                foreach (var device in searcher.Get())
                {
                    string caption = device["Caption"]?.ToString();
                    if (string.IsNullOrEmpty(caption)) continue;
                    if (!(caption.Contains("Arduino") || caption.Contains("USB Serial") ||
                          caption.Contains("CH340") || caption.Contains("CP210"))) continue;

                    int start = caption.LastIndexOf("(COM", StringComparison.Ordinal) + 1;
                    int end = caption.LastIndexOf(')');
                    if (start <= 0 || end <= start) continue;

                    string name = caption.Substring(start, end - start);
                    if (available.Length == 0 || available.Contains(name)) return name;
                }
            }
            catch { }
            return null;
        }

        void ClosePort()
        {
            lock (_writeGate)
            {
                var p = _port;
                _port = null;
                if (p == null) return;
                try { p.Dispose(); } catch { }
                _lineBuffer.Reset();
            }
        }

        public void Dispose() { Stop(); _cts?.Dispose(); }
    }
}
```

- [ ] **Step 6: `TrayApplicationContext`'i `SerialLink`'e taşı**

`serialPort` alanını (`:27`) kaldır, yerine:
```csharp
        private SerialLink serial;
```

`InitSerial` (`232-264`) ve `AutoDetectArduino` (`266-283`) metodlarını sil, yerine:
```csharp
        private void InitSerial()
        {
            serial = new SerialLink(log,
                preferredPort: () => config.ArduinoPort,
                baud: () => config.ArduinoBaud,
                autoDetect: () => string.IsNullOrEmpty(config.ArduinoPort));

            serial.Connected += () => {
                config.ArduinoPort = serial.CurrentPort;
                SendData("ON");
                UpdateLCD(forced: true);
                _ = PushArduinoData();
            };

            serial.LineReceived += line => {
                log.Debug("Arduino'dan gelen: " + line);
                if (line.StartsWith("EV:")) HandleArduinoEvent(line.Substring(3));
                else if (line == "CMD:READY" || line == "CMD:UPDATE") { UpdateLCD(true); _ = PushArduinoData(); }
                else if (line == "CMD:APPS" || line == "CMD:POOLS" || line == "CMD:SHORTCUTS") _ = PushArduinoData();
            };

            serial.Start();
        }
```

`SendData` (`504-510`) şu hâle gelir:
```csharp
        private void SendData(string msg) => serial?.Send(msg);
```

`UpdateLCD`'nin ilk satırındaki koruma:
```csharp
            if (serial == null || !serial.IsConnected) return;
```

`Reload` (`206-221`) içindeki seri bloğu:
```csharp
                serial?.Dispose();
                serial = null;
                if (config.EnableArduinoModule) InitSerial();
```

Çıkış yolunda (`cms.Items.Add("Çıkış", ...)`) `SendGoodbye()` çağrısından sonra ekle:
```csharp
                    serial?.Dispose();
                    (log as IDisposable)?.Dispose();   // ILog dispose edilebilir olmak zorunda değil
```

- [ ] **Step 7: Derle ve donanımla doğrula**

Run: `dotnet build KontroXXL.sln -c Debug` → hata yok.
Run: `dotnet run --project src/KontroXXL_WinApp`

Kabul testi (spec §6.4):
1. LCD normal çalışıyor.
2. **Arduino USB kablosunu çek.** LCD sönüyor, uygulama çökmüyor, log'a `Seri baglanti koptu` düşüyor.
3. 10 saniye bekle, **kabloyu tekrar tak.**
4. 5 saniye içinde LCD kendiliğinden geri geliyor — uygulamayı yeniden başlatmadan.

- [ ] **Step 8: Commit**

```bash
git add src/KontroXXL.Core/Serial src/KontroXXL_WinApp/SerialLink.cs tests/KontroXXL.Core.Tests/Serial src/KontroXXL_WinApp/TrayApplicationContext.cs
git commit -m "feat(serial): self-healing serial link with reconnect watchdog (A2)"
```

---

## Task 8: WinForms sızıntısını durdur (A1)

**Files:**
- Modify: `src/KontroXXL_WinApp/MainForm.cs` — `UpdateNasStats` (866-962), `CreateAppCtrl` (980-993), `Btn` (995-1004), `UpdateStats` (964-978)

**Interfaces:**
- Consumes: yok
- Produces: `static void MainForm.ClearAndDispose(Control.ControlCollection controls)` — Faz 4 silinene kadar tüm liste yenilemeleri bunu kullanır.

Bu görevin birim testi yoktur — WinForms kontrolleri test projesinden ulaşılamaz. Doğrulama Step 5'teki ölçümle yapılır ve **gerçek kabul kriteridir**.

- [ ] **Step 1: Dispose yardımcısını ve statik font cache'i ekle**

`MainForm` sınıfının içine, `BgDark` renk sabitlerinin (`220-224`) hemen altına:
```csharp
        // A1: WinForms'ta Controls.Clear() çocukları DISPOSE ETMEZ. Her yenilemede
        // Panel/Label/Button ve içlerindeki Font nesneleri sızıyordu -> GDI handle
        // tükeniyor, saatler içinde OutOfMemoryException geliyordu (Release_v2/crash.log).
        private static void ClearAndDispose(Control.ControlCollection controls)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                Control c = controls[i];
                controls.RemoveAt(i);
                c.Dispose();
            }
        }

        // A1: satır başına yeni Font yerine paylaşılan, hiç dispose edilmeyen tek örnekler.
        private static readonly Font FontRowTitle  = new Font("Segoe UI Bold", 9);
        private static readonly Font FontRowTitleL = new Font("Segoe UI Bold", 10);
        private static readonly Font FontRowState  = new Font("Segoe UI Bold", 8);
        private static readonly Font FontRowBody   = new Font("Segoe UI", 9);
        private static readonly Font FontRowSmall  = new Font("Segoe UI", 8);
        private static readonly Font FontRowButton = new Font("Segoe UI", 8, FontStyle.Bold);
        private static readonly Font FontSemibold9 = new Font("Segoe UI Semibold", 9);
```

- [ ] **Step 2: Dört `Controls.Clear()` çağrısını değiştir**

`MainForm.cs` içindeki şu dört satırı bul ve değiştir:

| Satır | Eski | Yeni |
|---|---|---|
| ~886 | `flowNasPools.Controls.Clear();` | `ClearAndDispose(flowNasPools.Controls);` |
| ~903 | `flowNasAlerts.Controls.Clear();` | `ClearAndDispose(flowNasAlerts.Controls);` |
| ~920 | `flowNasServices.Controls.Clear();` | `ClearAndDispose(flowNasServices.Controls);` |
| ~953 | `flowRealApps.Controls.Clear();` | `ClearAndDispose(flowRealApps.Controls);` |

- [ ] **Step 3: Satır içi `new Font(...)` çağrılarını statik alanlarla değiştir**

| Satır | Eski | Yeni |
|---|---|---|
| ~889 | `Font = new Font("Segoe UI Bold", 9)` | `Font = FontRowTitle` |
| ~905 | `Font = new Font("Segoe UI Semibold", 9)` | `Font = FontSemibold9` |
| ~909 | `Font = new Font("Segoe UI", 8)` | `Font = FontRowSmall` |
| ~928 | `Font = new Font("Segoe UI Bold", 9)` | `Font = FontRowTitle` |
| ~929 | `Font = new Font("Segoe UI Bold", 8)` | `Font = FontRowState` |
| ~936 | `Font = new Font("Segoe UI Bold", 8)` | `Font = FontRowState` |
| ~986 | `Font = new Font("Segoe UI Bold", 10)` | `Font = FontRowTitleL` |
| ~987 | `Font = new Font("Segoe UI", 9)` | `Font = FontRowBody` |
| ~999 | `Font = new Font("Segoe UI", 8, FontStyle.Bold)` | `Font = FontRowButton` |

**Not:** `InitializeComponent`, `BuildTopBar`, `BuildSideNav`, `Setup*Tab` içindeki `new Font(...)` çağrılarına **dokunma** — onlar bir kez çalışıyor ve sızmıyor.

- [ ] **Step 4: Derle**

Run: `dotnet build KontroXXL.sln -c Debug`
Expected: hata yok.

- [ ] **Step 5: Sızıntının bittiğini ölç — bu görevin gerçek testi**

1. `dotnet run --project src/KontroXXL_WinApp` ile başlat, dashboard'u aç.
2. Görev Yöneticisi → **Ayrıntılar** sekmesi → sütun başlığına sağ tıkla → **Sütun seç** → **GDI nesneleri** ve **USER nesneleri** ekle.
3. `KontroXXL_WinApp.exe` satırındaki **GDI nesneleri** değerini not al (T0).
4. 15 dakika boyunca NAS DASHBOARD ile NAS APPS sekmeleri arasında her 5 saniyede bir geçiş yap (veya sadece NAS DASHBOARD'da bırak — servis/alert listeleri kendiliğinden yenileniyor).
5. GDI değerini tekrar oku (T1).

Expected: **T1 − T0 ≤ 50.** Değişiklikten önce bu fark dakikada yüzlerce artıyordu. Fark hâlâ sürekli büyüyorsa `Controls.Clear()` kalıntısı aramak için: `grep -n "Controls.Clear()" src/KontroXXL_WinApp/MainForm.cs` — çıktı **boş olmalı**.

- [ ] **Step 6: Commit**

```bash
git add src/KontroXXL_WinApp/MainForm.cs
git commit -m "fix(ui): dispose cleared controls and share row fonts, stops the GDI leak behind the OOM crash (A1)"
```

---

## Task 9: `ConfigStore` — atomik yazım ve debounce'lu flush (A4)

**Files:**
- Create: `src/KontroXXL.Core/Configuration/JsonFileStore.cs`
- Test: `tests/KontroXXL.Core.Tests/Configuration/JsonFileStoreTests.cs`
- Modify: `src/KontroXXL_WinApp/Models.cs:50-66`

**Interfaces:**
- Consumes: yok
- Produces:
  - `static class JsonFileStore` — `static void WriteAtomic(string path, string content)`, `static string? ReadOrNull(string path)`
  - `AppConfig.Load(string path)` / `AppConfig.Save(string path)` — artık yolu parametre alır (Faz 2'nin `%APPDATA%` göçü buna dayanacak).
  - `AppConfig.MarkDirty()` / `AppConfig.FlushIfDirty(string path)`

- [ ] **Step 1: Başarısız testleri yaz**

`tests/KontroXXL.Core.Tests/Configuration/JsonFileStoreTests.cs`:
```csharp
using KontroXXL.Core.Configuration;
using Xunit;

namespace KontroXXL.Core.Tests.Configuration;

public class JsonFileStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "kx-cfg-" + Guid.NewGuid().ToString("N"));

    public JsonFileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void WriteAtomic_creates_the_file_with_the_given_content()
    {
        JsonFileStore.WriteAtomic(P("a.json"), "{\"x\":1}");
        Assert.Equal("{\"x\":1}", File.ReadAllText(P("a.json")));
    }

    [Fact]
    public void WriteAtomic_leaves_no_temp_file_behind()
    {
        JsonFileStore.WriteAtomic(P("a.json"), "{}");
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void WriteAtomic_replaces_existing_content_completely()
    {
        JsonFileStore.WriteAtomic(P("a.json"), "uzun-uzun-uzun-iceriik");
        JsonFileStore.WriteAtomic(P("a.json"), "kisa");
        Assert.Equal("kisa", File.ReadAllText(P("a.json")));
    }

    [Fact]
    public void WriteAtomic_creates_missing_directories()
    {
        string nested = Path.Combine(_dir, "x", "y", "a.json");
        JsonFileStore.WriteAtomic(nested, "{}");
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void ReadOrNull_returns_null_for_a_missing_file()
        => Assert.Null(JsonFileStore.ReadOrNull(P("yok.json")));

    [Fact]
    public void ReadOrNull_returns_content_for_an_existing_file()
    {
        File.WriteAllText(P("a.json"), "veri");
        Assert.Equal("veri", JsonFileStore.ReadOrNull(P("a.json")));
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `dotnet test --filter JsonFileStoreTests`
Expected: FAIL — `JsonFileStore` bulunamıyor (CS0246).

- [ ] **Step 3: `JsonFileStore`'u yaz**

`src/KontroXXL.Core/Configuration/JsonFileStore.cs`:
```csharp
using System.Text;

namespace KontroXXL.Core.Configuration;

/// <summary>
/// Yapılandırma dosyası yazımı. Doğrudan üzerine yazmak, yazma sırasındaki bir
/// çökmede config.json'ı yarım bırakır ve uygulama bir daha açılmaz.
/// Önce .tmp'ye yazılır, sonra tek adımda yerine taşınır.
/// </summary>
public static class JsonFileStore
{
    public static void WriteAtomic(string path, string content)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full) ?? ".";
        Directory.CreateDirectory(dir);

        string tmp = full + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(full)) File.Replace(tmp, full, destinationBackupFileName: null);
        else File.Move(tmp, full);
    }

    public static string? ReadOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test --filter JsonFileStoreTests`
Expected: PASS — 6 test.

- [ ] **Step 5: `AppConfig`'i yola-parametreli ve debounce'lu hâle getir**

`src/KontroXXL_WinApp/Models.cs:50-66` arasındaki `Load`/`Save`'i şununla değiştir:

```csharp
        // A8/A4: telemetri her saniye diske yazılmaz; kirli işaretlenir, flush timer'ı indirir.
        [JsonIgnore] private bool _dirty;
        [JsonIgnore] public string SourcePath { get; private set; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public void MarkDirty() => _dirty = true;

        public static AppConfig Load(string path = null)
        {
            path ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            string raw = KontroXXL.Core.Configuration.JsonFileStore.ReadOrNull(path);
            AppConfig cfg = null;
            if (raw != null)
            {
                try { cfg = JsonConvert.DeserializeObject<AppConfig>(raw); } catch { }
            }
            cfg ??= new AppConfig();
            cfg.SourcePath = path;
            return cfg;
        }

        public void Save()
        {
            KontroXXL.Core.Configuration.JsonFileStore.WriteAtomic(
                SourcePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            _dirty = false;
        }

        public void FlushIfDirty()
        {
            if (_dirty) Save();
        }
```

`Models.cs` başına `using Newtonsoft.Json;` zaten var; `JsonIgnore` için ek `using` gerekmez.

Ayrıca yeni interval alanlarını `AppConfig`'e ekle (`Shortcuts` alanının hemen üstüne):
```csharp
        // Faz 1 (A8): tek 500ms timer yerine ayrı periyotlar
        public int LcdIntervalMs { get; set; } = 200;
        public int PcIntervalMs { get; set; } = 1000;
        public int NasIntervalMs { get; set; } = 5000;
        public int ConfigFlushIntervalMs { get; set; } = 30000;
```

- [ ] **Step 6: Derle ve testleri koştur**

Run: `dotnet build KontroXXL.sln -c Debug && dotnet test`
Expected: hepsi yeşil.

- [ ] **Step 7: Commit**

```bash
git add src/KontroXXL.Core/Configuration tests/KontroXXL.Core.Tests/Configuration src/KontroXXL_WinApp/Models.cs
git commit -m "feat(config): atomic writes and dirty-flag flushing (A4)"
```

---

## Task 10: Zamanlamayı üçe ayır (A8) ve cache'i kalıcı kıl (A4)

**Files:**
- Modify: `src/KontroXXL_WinApp/TrayApplicationContext.cs:30,130-149,285-316`

**Interfaces:**
- Consumes: `AppConfig.LcdIntervalMs/PcIntervalMs/NasIntervalMs/ConfigFlushIntervalMs`, `MarkDirty()`, `FlushIfDirty()` (Task 9)
- Produces: yok (son entegrasyon görevi)

- [ ] **Step 1: Tek timer alanını dörde çıkar**

`:30` satırındaki `private System.Windows.Forms.Timer updateTimer;` yerine:
```csharp
        private System.Windows.Forms.Timer lcdTimer, pcTimer, nasTimer, flushTimer;
        private bool isPcUpdating = false;
        private bool isNasUpdating = false;
```

`isUpdatingData` alanını (`:48`) sil.

- [ ] **Step 2: Timer kurulumunu değiştir**

`130-149` arasındaki `updateTimer` bloğunu şununla değiştir:

```csharp
                // A8: v2'de tek 500ms timer her tick'te 8 TrueNAS isteği tetikliyordu.
                // Artık üç bağımsız periyot, hepsi config.json'dan ayarlanabilir.
                lcdTimer = new System.Windows.Forms.Timer { Interval = Math.Max(50, config.LcdIntervalMs) };
                lcdTimer.Tick += (s, e) => {
                    bool force = (DateTime.Now - lastHeartbeat).TotalSeconds > 4;
                    UpdateLCD(force);
                    if (force) lastHeartbeat = DateTime.Now;
                };
                lcdTimer.Start();

                pcTimer = new System.Windows.Forms.Timer { Interval = Math.Max(250, config.PcIntervalMs) };
                pcTimer.Tick += (s, e) => {
                    if (isPcUpdating) return;
                    isPcUpdating = true;
                    Task.Run(() => {
                        try { UpdatePcTelemetry(); }
                        catch (Exception ex) { log.Error("PC telemetri hatasi", ex); }
                        finally { isPcUpdating = false; }
                    });
                };
                pcTimer.Start();

                nasTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1000, config.NasIntervalMs) };
                nasTimer.Tick += (s, e) => {
                    if (isNasUpdating || !config.EnableNasModule || string.IsNullOrEmpty(config.TruenasIp)) return;
                    isNasUpdating = true;
                    Task.Run(async () => {
                        try { await UpdateNasTelemetry(); }
                        catch (Exception ex) { log.Error("NAS telemetri hatasi", ex); }
                        finally { isNasUpdating = false; }
                    });
                };
                nasTimer.Start();

                // A4: v2'de config.Save() yorum satırındaydı, Last* cache'i hiç diske inmiyordu.
                flushTimer = new System.Windows.Forms.Timer { Interval = Math.Max(5000, config.ConfigFlushIntervalMs) };
                flushTimer.Tick += (s, e) => {
                    try { config.FlushIfDirty(); }
                    catch (Exception ex) { log.Error("Config yazma hatasi", ex); }
                };
                flushTimer.Start();
```

- [ ] **Step 3: `UpdateSystemInfo`'yu ikiye böl**

`285-316` arasındaki `UpdateSystemInfo` metodunu şununla değiştir:

```csharp
        private void UpdatePcTelemetry()
        {
            int cpu = (int)GetCpuUsage(), ram = GetRamUsage();
            double net = GetPcNetSpeed(), ghz = GetCpuSpeed();
            var gpu = GetGpuInfo();
            int temp = GetPcTemp();

            config.LastCpu = cpu; config.LastRam = ram;
            config.LastNetSpeed = net; config.LastCpuFreq = ghz;
            config.LastGpu = gpu.Item1; config.LastGpuTemp = gpu.Item2; config.LastGpuFan = gpu.Item3;
            config.LastPcTemp = temp;
            config.MarkDirty();

            if (mainForm != null && !mainForm.IsDisposed)
                mainForm.UpdateStats(cpu, ram, gpu.Item1, gpu.Item2, gpu.Item3, ghz, net);
        }

        private async Task UpdateNasTelemetry()
        {
            var nas = await GetTruenasData();

            if (nas.conn && !nasLastConn)
            {
                log.Info("NAS baglantisi saglandi, Arduino guncelleniyor.");
                await PushArduinoData();
            }
            nasLastConn = nas.conn;
            if (!nas.conn) return;

            config.LastNasCpu = nas.nc; config.LastNasTemp = nas.nt;
            config.LastNasRx = nas.nrx; config.LastNasTx = nas.ntx;
            config.LastNasLoad = nas.nl; config.LastNasAlerts = nas.na;
            config.LastNasUptime = nas.up; config.LastNasMem = nas.mem;
            config.LastNasServicesJ = nas.svcs; config.LastNasAlertsJ = nas.alerts;
            config.LastPools = nas.pools; config.LastNasAppsJ = appsList;
            config.MarkDirty();
        }
```

- [ ] **Step 4: Çıkışta son flush'ı garanti et**

`cms.Items.Add("Çıkış", ...)` handler'ında `SendGoodbye()`'dan **önce** ekle:
```csharp
                    try { config.FlushIfDirty(); } catch { }
```

Aynı satırı `SystemEvents.SessionEnding` ve `PowerModeChanged` (Suspend) handler'larına da ekle (`118-120`).

- [ ] **Step 5: Derle ve doğrula**

Run: `dotnet build KontroXXL.sln -c Debug && dotnet test`
Expected: hepsi yeşil.

Elle doğrulama:
1. Uygulamayı çalıştır, 1 dakika bekle, tray'den **Çıkış**.
2. `bin/Debug/net8.0-windows/config.json` dosyasını aç → `LastCpu`, `LastGpuTemp` gibi alanlar **0 olmayan** güncel değerlerde olmalı (A4 kanıtı).
3. Tekrar başlat → dashboard açılır açılmaz dolu görünüyor, boş donut yok.
4. TrueNAS tarafında istek sıklığını gözle (veya `app.log`'daki `GetTruenasData` hata sıklığına bak) — artık 5 saniyede bir.

- [ ] **Step 6: Commit**

```bash
git add src/KontroXXL_WinApp/TrayApplicationContext.cs
git commit -m "perf: split the single 500ms loop into LCD/PC/NAS/flush timers (A8, A4)"
```

---

## Task 11: Dokümanları gerçeğe hizala ve Faz 1'i kapat

**Files:**
- Modify: `DOCS.md`, `TODO.md`, `PROJECT_SUMMARY.md`
- Create: `README.md`

**Interfaces:**
- Consumes: Task 1–10 çıktıları
- Produces: yok

- [ ] **Step 1: `TODO.md`'deki yanlış iddiaları düzelt**

`TODO.md` şu anda var olmayan üç özelliği "tamamlandı" gösteriyor. Faz 1 sonunda ikisi gerçek oldu, biri hâlâ yanlış:

- Satır 9 (`Türkçe Karakter Fix`) — artık **gerçek** (Task 2/6). Açıklamayı `LcdText.Sanitize` ile normalize ediliyor diye güncelle.
- Satır 15 (`Otomatik Dashboard: Dashboard artık esnek (resizable)`) — **yanlış**, `MainForm.cs:236` pencereyi kilitliyor. Bu maddeyi `- [ ]` yap ve `Faz 4 (Avalonia)` notu düş.
- `GELECEK PLANLARI` altına Faz 2/3/4 maddelerini ekle: Config şifreleme (Faz 2), versiyon kontrolü (Faz 2 — Velopack), NAS alert tray bildirimi (Faz 4).

- [ ] **Step 2: `DOCS.md`'yi güncelle**

- §3.1 başlatma akışını yeni sıraya göre yaz (`RollingFileLogger` → `SerialLink.Start()` → dört timer).
- §3.2'deki tek update döngüsünü üç döngüyle değiştir (LCD 200 ms / PC 1 s / NAS 5 s / flush 30 s).
- §5 altına yeni bir bölüm ekle: **"§5.3 Core kütüphanesi"** — `LcdText`, `LcdMenuModel`, `LcdFormatter`, `SerialLineBuffer`, `RollingFileLogger`, `JsonFileStore` ve katman kuralı (spec §4.1).
- §10 "Bilinen Kısıtlamalar" tablosundan `Auto-reconnect serial` ve `log rotate` satırlarını kaldır — artık çalışıyorlar.
- §11 "Sorun Giderme" tablosundan `app.log büyüklüğü / Elle sil` satırını kaldır.

- [ ] **Step 3: `README.md` yaz**

```markdown
# KontroXXL

Windows PC + TrueNAS telemetrisini 16×2 I²C LCD'ye basan tray uygulaması.

## Yapı

| Klasör | İçerik |
|---|---|
| `src/KontroXXL.Core` | Platform-bağımsız saf mantık — LCD biçimlendirme, menü durum makinesi, log, config. **Windows API'si içermez.** |
| `src/KontroXXL_WinApp` | WinForms arayüzü + tray + donanım erişimi (Faz 4'te Avalonia ile değişecek) |
| `tests/KontroXXL.Core.Tests` | xUnit |
| `firmware/arduino_kontrol` | ATmega328 firmware'i (`.ino`) |
| `docs/superpowers/` | Şartname ve faz planları |

## Derleme

```bash
dotnet build KontroXXL.sln -c Debug
dotnet test
dotnet run --project src/KontroXXL_WinApp
```

Gereksinim: .NET SDK 8.0

## Yapılandırma

`config.json` şu an exe'nin yanında oluşur (Faz 2'de `%APPDATA%\KontroXXL\` altına taşınıyor).
**API anahtarı düz metin tutuluyor — dosya `.gitignore`'da, asla commit etme.**

## Katman kuralı

`KontroXXL.Core` şu assembly'lere referans veremez: `System.Windows.Forms`,
`System.IO.Ports`, `System.Management`, `Microsoft.Win32.Registry`,
`AudioSwitcher.*`, `Avalonia`. Kural `ArchitectureTests` ile zorlanır.
```

- [ ] **Step 4: Faz 1 kabul kriterlerini koştur**

Spec §6'daki listeyi sırayla geç ve sonucu not al:

| # | Kriter | Nasıl |
|---|---|---|
| 1 | Release derlemesi temiz | `dotnet build KontroXXL.sln -c Release` |
| 2 | Testler yeşil | `dotnet test` |
| 3 | Arduino ve NAS kapalıyken açılıyor | `config.json`'da her iki modülü `false` yap, çalıştır |
| 4 | Kablo çıkar/tak → LCD geri geliyor | Task 7 Step 7 |
| 5 | GDI sabit | Task 8 Step 5 |
| 6 | Log 1 MB'ı aşmıyor | Uzun çalışmadan sonra `bin/.../app.log` boyutu |
| 7 | Her kare 16 karakter | `dotnet test --filter Every_frame_is_exactly_16` |

- [ ] **Step 5: Commit ve etiketle**

```bash
git add DOCS.md TODO.md PROJECT_SUMMARY.md README.md
git commit -m "docs: align documentation with the code after phase 1"
git tag -a v2.1.0 -m "Faz 1: sağlamlaştırma — sızıntı, yeniden bağlanma, log rotasyonu, test edilebilir LCD çekirdeği"
```

---

## Faz 1 sonunda ne olacak

- `Release_v2/crash.log`'daki `OutOfMemoryException`'ın kök nedeni (A1) kapandı.
- Arduino kopunca uygulama kendi kendini toparlıyor (A2).
- `app.log` en fazla 4 MB yer kaplıyor (1 MB × mevcut + 3 arşiv) (A3).
- Telemetri cache'i gerçekten diske iniyor (A4).
- LCD mantığında thread yarışı yok, index taşması yok (A7).
- TrueNAS saniyede 2 yerine 5 saniyede 1 sorgulanıyor (A8).
- Türkçe karakterler LCD'de `?` olarak çıkmıyor (A10).
- ~140 test vakası, hepsi `KontroXXL.Core` üzerinde, donanım gerektirmiyor.

**Sonraki:** Faz 2 planı (`%APPDATA%` göçü, DPAPI, Velopack, avrdude firmware flash) Faz 1 tamamlandıktan sonra yazılacak — çünkü göç kodu Task 9'un `AppConfig.Load(path)` imzasına dayanıyor ve o imzanın gerçek hâlini görmeden plan yazmak tahmin olur.
