# KontroXXL v3 — Teknik Şartname (Spec)

> **Tarih:** 2026-09-01
> **Durum:** Onaylandı (Karaduman)
> **Kapsam kararı:** Faz 1 + 2 + 3 + 4 (dördü de)
> **Mevcut sürüm:** v2 / "Thin Client v7.8" — `Release_v2/`

---

## 1. Amaç

KontroXXL, Windows PC + TrueNAS telemetrisini 16×2 I²C LCD'ye basan, tray'de
çalışan bir masaüstü uygulaması. Bu şartname üç şeyi hedefliyor:

1. **Kararlılık** — uygulama şu anda saatler içinde `OutOfMemoryException` ile
   çöküyor (`Release_v2/crash.log`), Arduino çıkarılınca geri bağlanmıyor,
   log dosyası sonsuza kadar büyüyor.
2. **Dağıtılabilirlik** — bugün "kurulum" bir klasörü kopyalamaktan ibaret.
   Hedef: tek tıkla kurulum, otomatik güncelleme, Arduino firmware'inin de
   kurulum tarafından yüklenmesi.
3. **Sürdürülebilirlik** — 757 satırlık tanrı sınıf ve elle piksel yerleştirilmiş
   1095 satırlık WinForms arayüzü, sıfır test.

---

## 2. Onaylanan kararlar

| Konu | Karar | Gerekçe |
|---|---|---|
| Kapsam | Faz 1 → 2 → 3 → 4, sırayla | Her faz tek başına çalışan yazılım üretir |
| Arayüz | **Avalonia 11'e taşı** | Layout motoru + DPI + MVVM; ileride Linux ihtimali açık kalır |
| Kurulum | **Velopack** | Kurulum **ve** delta otomatik güncelleme; TODO'daki "versiyon kontrolü" maddesini de kapatır |
| Firmware | **avrdude ile kurulumdan flash** | Manuel Arduino IDE adımı tamamen kalkar |
| .NET | **net8.0 / net8.0-windows** | Makinede yalnızca SDK 8.0.301 kurulu (`dotnet --list-sdks`). .NET 10 LTS'e çıkmak ayrı bir SDK kurulumu gerektirir; kapsam dışı. |
| JSON | Faz 3'te `System.Text.Json` + tipli DTO | `JToken` indeksleme her yerde null-patlaması riski |
| Donanım telemetrisi | Faz 3'te `LibreHardwareMonitorLib` | `nvidia-smi` process spawn'ını, AMD/Intel boşluğunu ve WMI sıcaklık sorununu tek seferde çözer |
| Ses | Faz 3'te `NAudio.Wasapi` | `AudioSwitcher` .NET Framework paketi (NU1701 bastırılmış) |

### 2.1 Kasıtlı olarak YAPILMAYACAKLAR (YAGNI)

- WinForms arayüzünde kozmetik iyileştirme **yapılmaz** (DPI manifest, resizable
  pencere, özel scrollbar düzeltmesi, tema). Faz 4 bu kodu tamamen siliyor.
  Faz 1'de WinForms'a yalnızca **çökmeyi durduran** minimum müdahale yapılır.
- ESP32 / WiFi'ye geçiş yapılmaz — ürünü baştan tanımlar.
- Kod imzalama sertifikası alınmaz. SmartScreen uyarısı kabul edilir.
- Çoklu NAS (`Truenas2*`) desteği canlandırılmaz; `config.json`'daki ölü alanlar
  Faz 2'de temizlenir.

---

## 3. Mevcut durum — kanıtlı bulgular

Her satır kod referansıyla doğrulandı.

### 3.1 Kritik

| # | Bulgu | Kanıt |
|---|---|---|
| A1 | **GDI/bellek sızıntısı → OOM çökmesi.** `flowX.Controls.Clear()` çocukları dispose etmiyor; her satırda `new Font(...)`; projede tek `Dispose()` çağrısı yok. | `MainForm.cs:886,903,920,953` + `889,905,928,986`; `Release_v2/crash.log` |
| A2 | **Seri port yeniden bağlanma yok.** `InitSerial()` yalnızca ctor ve `Reload()`'da. Arduino çıkarılınca uygulama sessizce ölü kalır. | `TrayApplicationContext.cs:85,216` |
| A3 | **Log rotasyonu çalışmıyor.** `.bak`'a kopyalıyor, orijinali silmiyor, `append:true` ile açıyor → `app.log` sonsuz büyür (kökte 432 KB). | `TrayApplicationContext.cs:76-78` |
| A4 | **Açılış cache'i kırık.** `config.Save()` yorum satırında; `Last*` alanları yalnızca ayar/kısayol kaydında diske iniyor. | `TrayApplicationContext.cs:312` |
| A5 | **API anahtarı düz metin**, exe'nin yanında ve proje kökünde. | `config.json:5` |
| A6 | **Yazılabilir durum kurulum dizininde.** `Program Files`'a veya Velopack'in `current/` klasörüne kurulunca kaybolur/patlar. | `Models.cs:52,64`; `TrayApplicationContext.cs:61` |
| A7 | **Thread güvenliği yok.** `lcdIndex`/`currentLcdMode`/`appsList` seri thread ile timer thread arasında kilitsiz. `appsList[lcdIndex]` liste küçülürse patlar, try/catch yutar, LCD donar. | `TrayApplicationContext.cs:36,40,446`; `MainForm.cs:867` |
| A8 | **NAS saniyede iki kez dövülüyor.** 500 ms timer her tick'te 8 paralel TrueNAS isteği tetikliyor. | `TrayApplicationContext.cs:131` |
| A9 | **`"COM4"` sihirli değeri** "otomatik algıla" anlamına geliyor; gerçekten COM4'teki cihaz sürekli eziliyor. | `TrayApplicationContext.cs:235` |
| A10 | **Türkçe→ASCII normalizasyonu hiç yok** (TODO.md aksini iddia ediyor). `SerialPort` varsayılan ASCII encoding kullandığı için "Müzik Çalar" → `M?zik ?alar`. | `TrayApplicationContext.cs` genelinde `Normalize` yok; `config.json:20` `"CİFS, SSH"` |

### 3.2 Doküman/gerçek uyuşmazlıkları

`DOCS.md` ve `TODO.md` şunları var diyor, kodda **yok**: auto-reconnect serial
(A2), log rotate (A3), Türkçe karakter fix (A10), resizable dashboard
(`MainForm.cs:236` `MinimumSize == MaximumSize`). Faz 1 sonunda dokümanlar
gerçeğe göre düzeltilecek.

### 3.3 Arayüz

- DPI farkındalığı yok (`app.manifest` dosyası yok, tüm konumlar sabit piksel).
- Pencere 1000×680'e kilitli; borderless olduğu için Aero Snap yok.
- `"● LIVE"` etiketi sahte — `MainForm.cs:313`'te oluşturuluyor, bir daha
  hiç dokunulmuyor (kodda `lblStatus` tek bir yerde geçiyor).
- Özel scrollbar 80 ms `Timer` ile çiziliyor.
- `LineChart` 50 nokta, zaman ekseni yok, jenerik kontrolün içine `"Mbps"` gömülü.
- Klavye erişimi yok — nav düğmeleri `Click`'li `Panel`.
- Emoji ikonlar (🏠📊📦🚀⚙️) sistemden sisteme farklı çiziliyor.

---

## 4. Hedef mimari

```
nas-lcd/
├─ KontroXXL.sln
├─ Directory.Build.props            ← ortak sürüm, LangVersion, Nullable
├─ src/
│  ├─ KontroXXL.Core/               (net8.0)          platform-bağımsız, %100 test edilebilir
│  │  ├─ Configuration/  AppConfig, ConfigStore, AppPaths, ISecretProtector
│  │  ├─ Logging/        RollingFileLogger, ILog
│  │  ├─ Lcd/            LcdFormatter, LcdMenuModel, LcdFrame, LcdText
│  │  ├─ Truenas/        TruenasClient, Dto/*
│  │  └─ Telemetry/      ITelemetrySource, TelemetrySnapshot
│  ├─ KontroXXL.Windows/            (net8.0-windows)  WMI, PerfCounter, WASAPI, SerialPort, Registry
│  │  ├─ SerialLink, WindowsTelemetrySource, AudioController,
│  │  ├─ DpapiSecretProtector, StartupRegistration, FirmwareFlasher
│  ├─ KontroXXL.App/                (net8.0-windows)  Avalonia UI + tray + composition root
│  └─ KontroXXL.WinFormsLegacy/     Faz 4 sonunda SİLİNİR
├─ tests/
│  └─ KontroXXL.Core.Tests/         (xUnit)
├─ firmware/
│  ├─ arduino_kontrol/arduino_kontrol.ino
│  └─ build-firmware.ps1            → arduino_kontrol.ino.hex
├─ installer/
│  ├─ pack.ps1                      → vpk pack
│  └─ tools/avrdude/                avrdude.exe + avrdude.conf
└─ docs/
```

### 4.1 Katman kuralı

`KontroXXL.Core` **hiçbir** Windows API'sine, `System.Windows.Forms`'a,
Avalonia'ya veya `System.IO.Ports`'a referans veremez. Donanıma dokunan her şey
Core'da bir arayüzün arkasında durur, `KontroXXL.Windows` implemente eder.
Bu kural testlerin var olma sebebidir; ihlal edilirse plan çöker.

### 4.2 Dosya yolları

Velopack uygulamayı `%LOCALAPPDATA%\KontroXXL\current\` altına kurar ve her
güncellemede bu klasörü **değiştirir**. Yazılabilir hiçbir şey oraya konamaz.

| İçerik | Yol |
|---|---|
| Yapılandırma | `%APPDATA%\KontroXXL\config.json` |
| Loglar | `%APPDATA%\KontroXXL\logs\app.log` (+ `app.1.log` … `app.3.log`) |
| Firmware | `<kurulum>\firmware\arduino_kontrol.ino.hex` (salt okunur) |

Faz 2 ilk açılışta eski konumlardan (exe yanı **ve** eski proje kökü) tek seferlik
göç yapar ve `config.json`'a `SchemaVersion: 3` yazar.

### 4.3 Zamanlama (A8 çözümü)

Tek 500 ms timer yerine üç bağımsız periyot, hepsi `config.json`'dan ayarlanabilir:

| Döngü | Varsayılan | Yaptığı |
|---|---|---|
| LCD | 200 ms | Kayan yazı/ticker animasyonu, çerçeve karşılaştırıp değişeni gönderme |
| PC telemetrisi | 1000 ms | CPU/RAM/GPU/ağ |
| NAS anketi | 5000 ms | TrueNAS REST |
| Config yazımı | 30 000 ms (debounce) | `Last*` cache'i diske indirir (A4) |

### 4.4 LCD sözleşmesi

- Her çerçeve **tam olarak 16 karakter** iki satır. Bu bir invaryanttır ve
  testle zorlanır.
- Yalnızca ASCII (0x20–0x7E) + iki özel karakter: `\x01` = RX oku (aşağı),
  `\x02` = TX oku (yukarı). Türkçe karakterler gönderilmeden önce
  translitere edilir (A10): `ı→i ğ→g ü→u ş→s ö→o ç→c İ→I Ğ→G Ü→U Ş→S Ö→O Ç→C`.
- Seri protokol v2'den değişmez: `L0=`, `L1=`, `B1=`, `CLR`, `ON`, `OFF`
  (PC→Arduino); `EV:UP`, `EV:DN`, `EV:CLICK`, `EV:BACK`, `CMD:READY`,
  `CMD:UPDATE`, `CMD:APPS`, `CMD:POOLS`, `CMD:SHORTCUTS` (Arduino→PC).
  **Firmware'in yeniden yazılması bu projede kapsam dışıdır** — yalnızca
  derlenip `.hex` olarak paketlenir.

---

## 5. Fazlar

### Faz 1 — Sağlamlaştırma (WinForms korunur)
Çökmeleri ve sızıntıyı durdurur, `KontroXXL.Core` + test altyapısını kurar.
Bitişte: uygulama günlerce ayakta kalır, LCD mantığı %100 test kapsamında.
**Kapsar:** A1, A2, A3, A4, A7, A8, A10 + git + testler.

### Faz 2 — Kurulum ve güvenlik
**Kapsar:** A5, A6, A9 + sürümleme + Velopack + firmware flash adımı.
Bitişte: `KontroXXL-Setup.exe` çift tıkla kurar, uygulama içinden güncellenir,
Ayarlar'dan Arduino programlanır.

### Faz 3 — Mimari
Tanrı sınıfın parçalanması, tipli DTO'lar, `LibreHardwareMonitorLib`,
`NAudio.Wasapi`, `System.Text.Json`.
Bitişte: `TrayApplicationContext` yalnızca kablolama yapar; Core testleri
TrueNAS JSON fixture'larını kapsar.

### Faz 4 — Avalonia arayüzü
MVVM ile beş görünüm, gerçek durum göstergesi, tema, klavye erişimi,
tray bildirimleri. Bitişte: `KontroXXL.WinFormsLegacy` silinir.

---

## 6. Kabul kriterleri (faz bağımsız)

1. `dotnet build KontroXXL.sln -c Release` sıfır uyarı-hatasıyla geçer.
2. `dotnet test` yeşil.
3. Uygulama Arduino takılı değilken de açılır ve çalışır (NAS modülü de kapalıyken).
4. Arduino kablosu çıkarılıp 10 sn sonra takıldığında LCD kendiliğinden geri gelir.
5. 24 saatlik çalışmadan sonra process'in Private Bytes değeri başlangıcın
   %150'sini aşmaz ve GDI Objects sayısı sabit kalır (Görev Yöneticisi → Ayrıntılar).
6. `%APPDATA%\KontroXXL\logs\app.log` 1 MB'ı aşmaz.
7. LCD'ye giden hiçbir çerçeve 16 karakterden farklı uzunlukta değildir.

---

## 7. Bilinen riskler

| Risk | Etki | Azaltma |
|---|---|---|
| `LibreHardwareMonitorLib` bazı sensörler için yönetici hakkı ister | GPU/anakart sıcaklığı 0 döner | Yönetici değilse sessizce `nvidia-smi`'ye düş; Ayarlar'da bilgilendir |
| Velopack ilk kurulumdan sonra `AppData` göçünü tetiklemez | Ayarlar kaybolur gibi görünür | Göç kodu her açılışta idempotent çalışır, `SchemaVersion` ile korunur |
| avrdude yanlış porta yazar | Başka bir cihaz bozulabilir | Flash öncesi `Win32_PnPEntity` ile cihazın Arduino/CH340/CP210x olduğu doğrulanır; kullanıcıdan onay alınır |
| Avalonia geçişinde WinForms davranışı kaybolur | Regresyon | Faz 4 boyunca iki arayüz yan yana çalışır; WinForms yalnızca son görevde silinir |
| Test edilemeyen kod (WMI, seri, ses) | Yanlış güven | Bunlar Core'da arayüzün arkasında; testler sahte implementasyonla yazılır, gerçek donanım kabul kriterleriyle elle doğrulanır |

---

## 8. Derhal yapılması gereken (kod dışı)

**TrueNAS API anahtarını iptal et ve yenisini üret.** Mevcut anahtar
(`config.json:5`) düz metin olarak proje kökünde duruyor ve tam yetkili.
Faz 2 şifrelemeyi getiriyor ama sızmış bir anahtarı şifrelemek işe yaramaz.
