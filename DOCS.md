# KontroXXL — Developer Documentation

> **Son Güncelleme:** 2026-09-02 | **Versiyon:** v3 (Faz 1: sağlamlaştırma) | **Platform:** Windows 10/11 · .NET 8.0 · Arduino ATmega328

---

## 1. Sistem Mimarisi

```
┌──────────────────────────────────────────────────────────┐
│                  Windows PC (C# .NET 8.0)                │
│                                                          │
│  TrayApplicationContext.cs ◄──► MainForm.cs             │
│  • Telemetri timer (1s)         • PC Dashboard          │
│  • GPU / CPU / RAM              • NAS Dashboard         │
│  • TrueNAS REST API             • NAS Apps              │
│  • Serial port (Arduino)        • Quick Actions         │
│  • Audio control                • System Config         │
│  • LCD komut üretimi                                    │
└──────────────────┬───────────────────────────────────────┘
                   │ USB Serial (115200 baud)
                   ▼
        arduino_kontrol.ino (ATmega328)
        • Komut parse (char[], strncmp)
        • Rotary encoder + buton
        • 16×2 I²C LCD sürücü
                   │ I²C (0x27)
                   ▼
              [16×2 LCD Ekran]
```

---

## 2. Dosya Haritası

Faz 1 (Task 1) klasör yapısını `src/`/`tests/`/`firmware/` altına taşıdı — aşağıdaki ağaç `git ls-files` ile doğrulanmış güncel hâldir:

```
nas-lcd/
├── src/
│   ├── KontroXXL_WinApp/              ← WinForms uygulaması (Faz 4'te Avalonia ile değişecek)
│   │   ├── TrayApplicationContext.cs  ← ANA MOTOR
│   │   ├── MainForm.cs                ← UI
│   │   ├── Models.cs                  ← AppConfig, ShortcutItem
│   │   ├── SerialLink.cs              ← Arduino seri bağlantısı, yeniden bağlanma (A2)
│   │   ├── Program.cs                 ← Entry point + mutex
│   │   └── KontroXXL_WinApp.csproj
│   └── KontroXXL.Core/                ← Platform-bağımsız saf mantık (Task 1), bkz. §5.3
│       ├── Lcd/, Logging/, Serial/, Configuration/
│       └── KontroXXL.Core.csproj
├── tests/
│   └── KontroXXL.Core.Tests/          ← xUnit, 150 test, donanım gerektirmez
├── firmware/
│   ├── arduino_kontrol/arduino_kontrol.ino
│   └── eski-versiyon.ino.txt          ← v2 öncesi referans
├── tools/
│   └── IconGen.cs/.csproj             ← Tek seferlik ikon aracı
├── docs/superpowers/                  ← Şartname ve faz planları
├── KontroXXL.sln
└── OPTIMIZATIONS.md                   ← 26/26 bulgu tamamlandı
```

`config.json` ve `app.log` derleme çıktısının (`bin/.../`) yanında runtime'da oluşur — `Release_v2/` artık depoda değil, `.gitignore`'da.

---

## 3. TrayApplicationContext.cs — Ana Motor

### 3.1 Başlatma Akışı

Faz 1'de yeniden yazıldı (Task 6/7/9/10). Gerçek sıra, `TrayApplicationContext()` kurucusundan:

```
Program.Main()
  └─► new TrayApplicationContext()
        ├─ new RollingFileLogger("app.log")   // A3: gerçekten döner, açılamazsa NullLog'a düşer
        ├─ AppConfig.Load()                   // config.json oku (KontroXXL.Core.Configuration.JsonFileStore üzerinden)
        ├─ InitSerial() → serial.Start()      // A2: SerialLink arka plan thread'inde bağlanmayı dener (yalnızca EnableArduinoModule=true ise)
        ├─ new HttpClientHandler()            // SSL bypass: sadece TruenasIp'e
        ├─ new CoreAudioController()          // ses cihazı
        ├─ PerformanceCounter × 3             // CPU%, CPU freq, RAM — bir kez oluşturulur
        ├─ new MainForm(config)
        ├─ tray ikonu + context menu + SystemEvents handler'ları (PowerModeChanged/SessionEnding/SessionEnded)
        └─ dört timer'ı başlat: lcdTimer, pcTimer, nasTimer, flushTimer
```

`SerialLink.Start()` senkron değildir — bağlantı 2 saniyelik bir izleyici döngüsünde arka planda kurulur (A2), kurucu onu beklemez.

### 3.2 Dört Bağımsız Döngü (Task 10, A8/A4)

v2'nin tek 500 ms `updateTimer`'ı (her tick'te 8 TrueNAS isteği) kaldırıldı. Yerine dört `System.Windows.Forms.Timer`, hepsi `config.json`'dan ayarlanabilir:

| Timer | Alan (config.json) | Varsayılan | Görev |
|---|---|---|---|
| `lcdTimer` | `LcdIntervalMs` | 200 ms | `UpdateLCD()` — LCD kare üretir ve seri porta yazar |
| `pcTimer` | `PcIntervalMs` | 1000 ms | `UpdatePcTelemetry()` — CPU/RAM/GPU/Net/Isı, `Task.Run` üzerinden arka planda |
| `nasTimer` | `NasIntervalMs` | 5000 ms | `UpdateNasTelemetry()` → `GetTruenasData()` — `Task.WhenAll` ile 8 endpoint paralel, yalnızca `EnableNasModule && TruenasIp` doluysa |
| `flushTimer` | `ConfigFlushIntervalMs` | 30000 ms | `config.FlushIfDirty()` — kirli işaretliyse `config.json`'ı diske yazar |

Her interval `Math.Max(alt sınır, config.XyzIntervalMs)` ile korunur (LCD ≥ 50 ms, PC ≥ 250 ms, NAS ≥ 1000 ms, flush ≥ 5000 ms) — çok küçük bir değer LCD'yi veya ağı boğamaz.

`pcTimer` ve `nasTimer`, telemetri yazımlarını `lock (config.SyncRoot)` altında yapar; `AppConfig.Save()` da serileştirmeyi aynı kilit altında yapar (Task 9/10) — arka plan thread'inden yazılan `Last*` alanları ile UI thread'inden tetiklenen `Save()` artık yarışmaz.

`UpdateLCD()` LCD durumuna dokunan tek yerdir ve yalnızca UI thread'inde çalışır: seri porttan gelen `EV:*`/`CMD:*` olayları `BeginInvoke` ile UI thread'ine sıraya alınır (A7).

### 3.3 TrueNAS API (Paralel)

```csharp
// Eskisi: sıralı ~3-5 sn  →  Yeni: paralel ~0.5-1 sn
await Task.WhenAll(
    httpClient.GetStringAsync(".../system/info"),
    TruenasPost("reporting/get_data", cpu),
    TruenasPost("reporting/get_data", cputemp),
    httpClient.GetStringAsync(".../pool"),
    httpClient.GetStringAsync(".../alert/list"),
    httpClient.GetStringAsync(".../service")
);
```

### 3.4 SSL Güvenliği (F19)

```csharp
// Sadece TrueNAS IP'ine bypass — diğer tüm HTTPS normal doğrulama
ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => {
    if (msg.RequestUri?.Host == config.TruenasIp) return true;
    return errors == SslPolicyErrors.None;
};
```

---

## 4. LCD Protokolü

### 4.1 PC → Arduino Komutları (Serial, `\n` sonlandırmalı)

`firmware/arduino_kontrol/arduino_kontrol.ino`'daki `strcmp`/`strncmp` dallarıyla doğrulandı:

| Komut | Açıklama | Örnek |
|-------|----------|-------|
| `L0=<16char>` | Satır 0 yaz | `L0=CPU:76%   3.60G` |
| `L1=<16char>` | Satır 1 yaz | `L1=RAM:42%   04:35` |
| `B1=<0-100>` | Bar grafik | `B1=72` |
| `CLR` | Ekranı temizle | — |
| `ON` / `OFF` | Backlight fade in/out | Çıkışta `SendGoodbye()` `OFF` gönderir |

> Not: bu doküman daha önce burada bir `PING`/`PONG` komutu listeliyordu; firmware'de böyle bir komut yok, kaldırıldı.

### 4.2 Arduino → PC Mesajları

| Mesaj | Tetikleyici |
|-------|-------------|
| `EV:UP` | Encoder saat yönü |
| `EV:DN` | Encoder ters yön |
| `EV:CLICK` | Buton basışı |
| `EV:BACK` | Geri butonu |
| `CMD:READY` | Açılışta veya 2 sn içinde PC'den veri gelmezse — Arduino "bağlantı bekleniyor" moduna girdiğini bildirir |

> Not: bu doküman daha önce `VOL+`/`VOL-`/`BTN`/`LONGBTN` listeliyordu; gerçek mesajlar yukarıdaki `EV:*`/`CMD:READY`. `TrayApplicationContext.cs` ayrıca `CMD:UPDATE`/`CMD:APPS`/`CMD:POOLS`/`CMD:SHORTCUTS`'ı da işler, ama mevcut firmware bunları hiç göndermiyor (PC tarafında ileriye dönük hazırlık).

### 4.3 LCD Sayfaları (Home Mode)

| Page | Satır 0 | Satır 1 |
|------|---------|---------|
| 0 | `CPU:76%   3.60G` ← sağa yaslı frekans | `RAM:42%   04:35` ← sağa yaslı saat |
| 1 | `GPU:45% 68C` | `Fan:30% 7Mbps` |
| 2 | `NAS:55% 42C` | `↑3Mb ↓0Mb` |
| 3 | `> NAS DASHBOARD` | `2 SYSTEM ALERTS!` |

> Sağa yaslama: `PadLeft(16 - leftSide.Length)` — rakamlar değişmez, sadece boşluk eklenir.

### 4.4 LCD Modları

Faz 1'den beri bu durum makinesi `KontroXXL.Core.Lcd.LcdMenuModel` içinde saf bir fonksiyon (`Apply`), bkz. §5.3.

```csharp
enum LcdMode { Home, Menu, Apps, Pools, Shortcuts, NasPower }
// Home:       döngüsel sayfa (encoder Up/Down → ses; Back → sayfa ileri)
// Menu:       kısa basış (Click) açar, 4 seçenek: NAS APPS / NAS POOLS / SHORTCUTS / NAS POWER
// Apps:       NAS app listesi, Click → start/stop toggle
// Pools:      storage pool doluluk (yalnızca görüntüleme, Click etkisiz)
// Shortcuts:  kısayol listesi, Click → çalıştır ve Home'a dön
// NasPower:   REBOOT / SHUTDOWN / CANCEL, Click → NAS'a komut gönder ve Home'a dön
```

### 4.5 Volüm Override

Encoder çevrilir → `volumeShowUntil = now + 3s` → 3 sn boyunca ses bar grafiği → eski sayfaya dön.

---

## 5. Arduino Firmware

### 5.1 Bellek Yönetimi (Çok Önemli)

ATmega328 = **2KB SRAM**. `String` sınıfı heap fragmantasyonu yapar → **char[] kullanılıyor**.

```cpp
char inputBuffer[32];   // stack'te — heap alloc yok
uint8_t bufIdx = 0;

void handleCommand(const char* cmd) {             // char*  — String değil
    if      (strncmp(cmd, "L0=", 3) == 0) { ... }
    else if (strncmp(cmd, "L1=", 3) == 0) { ... }
    else if (strncmp(cmd, "B1=", 3) == 0) { drawBar(atoi(cmd + 3)); }
}

void lcdPad(const char* s) {    // doğrudan LCD'ye yazar, String oluşturmaz
    uint8_t n = 0;
    for (; s[n] && n < 16; n++) lcd.write(s[n]);
    for (; n < 16; n++) lcd.write(' ');
}
```

### 5.2 Pin Bağlantıları

| Pin | Bileşen |
|-----|---------|
| A4 (SDA) | LCD I²C |
| A5 (SCL) | LCD I²C |
| 2 | Rotary CLK |
| 3 | Rotary DT |
| 4 | Rotary Buton |

### 5.3 Core Kütüphanesi

Faz 1'de (Task 1) `src/KontroXXL.Core/` adında yeni, `net8.0` (Windows'a bağımlı olmayan) bir class library eklendi. Amaç: LCD biçimlendirme, menü durum makinesi, log ve config yazımı gibi saf mantığı donanım ve UI'dan ayırıp birim testlerine açmak (v2'de bunların hepsi `TrayApplicationContext.cs` içinde, test edilemez şekilde iç içeydi).

| Dosya | Sorumluluk |
|---|---|
| `Lcd/LcdText.cs` | `Sanitize`/`Fit`/`Scroll` — Türkçe karakter translitasyonu ve 16 karakter genişlik garantisi |
| `Lcd/LcdMenuModel.cs` | `LcdMenuModel.Apply` — girdiye (Up/Down/Click/Back) göre saf durum geçişi, index her zaman clamp'lenir (A7) |
| `Lcd/LcdFormatter.cs` | `LcdMenuState` + `LcdViewData` + `LcdRenderContext` → `LcdFrame` (iki satır, opsiyonel bar değeri) |
| `Lcd/LcdFrame.cs` | Ekrana giden tek kare — `Line0`/`Line1` her zaman tam 16 karakter, `BarValue` doluysa çağıran L1 yerine B1 gönderir |
| `Lcd/LcdViewData.cs` | Formatter'ın ihtiyaç duyduğu değişmez anlık görüntü + `LcdRenderContext` (zaman/kaydırma gibi yan etkiler formatter dışında tutulur) |
| `Logging/ILog.cs` | `ILog` arayüzü + `NullLog` (log açılamadığında veya testte kullanılan no-op) |
| `Logging/RollingFileLogger.cs` | Gerçekten dönen dosya logu — `app.log` → `app.1.log` → `app.2.log` → `app.3.log`, seviye filtresi (A3) |
| `Serial/SerialLineBuffer.cs` | Ham bayt akışını `\n` sınırlarında satırlara böler, taşmada satırı düşürür — `SerialPort.ReadLine()`'ın bloklayan/istisna fırlatan davranışı yerine |
| `Configuration/JsonFileStore.cs` | `WriteAtomic`/`ReadOrNull` — `.tmp`'ye yaz, `File.Replace` ile yerine taşı; yarım yazımda `config.json` bozulmaz (A4) |

**Katman kuralı (spec §4.1):** `KontroXXL.Core`, `System.Windows.Forms`, `System.IO.Ports`, `System.Management`, `Microsoft.Win32.Registry`, `AudioSwitcher.*` veya `Avalonia`'ya referans veremez. Kural `tests/KontroXXL.Core.Tests/ArchitectureTests.cs`'teki `Core_does_not_reference_platform_assemblies` testiyle derleme sonrası zorlanır — Core Assembly'sinin referans listesi bu yasaklı listeyle kesiştirilir.

`tests/KontroXXL.Core.Tests/` bu kütüphanenin xUnit test projesidir (150 test, donanım gerektirmez).

---

## 6. MainForm.cs — UI

### 6.1 Kontrol Hiyerarşisi

```
Form (1000×680, sabit boyut — MinimumSize == MaximumSize, FormBorderStyle.None, yuvarlak köşe)
├── topBar (Dock=Top, H=40) — sürükle/taşı olayı burada
├── sideNav (Dock=Left, W=220) — 5 nav butonu
└── contentContainer (Dock=Fill)
    ├── [Dashboard]     NoScrollPanel
    │   ├── fDonuts (FlowLayout, Top, H=210)
    │   │   ├── pCpu ─► dntPcCpu + lblCpuFreq
    │   │   ├── dntPcRam, dntPcGpu, dntPcGpuTemp, dntPcGpuFan
    │   └── pnDetail ─► lblPcNet + chartPcNet
    ├── [NasDashboard]  NoScrollPanel
    │   ├── dntNasCpu, dntNasTemp, pn(info+graph+buttons)
    │   ├── flowNasPools, flowNasApps, flowNasServices
    ├── [NasApps]       NoScrollPanel ─► flowRealApps
    ├── [Shortcuts]     Panel ─► lstShortcuts + ekle/sil
    └── [Settings]      NoScrollPanel ─► form alanları
```

### 6.2 Custom Controls

| Sınıf | Açıklama |
|-------|----------|
| `DonutProgress` | Halka progress. `static readonly Font` ile GDI leak önlendi. |
| `LineChart` | Circular ring buffer (50 nokta). O(1) insert, O(1) snapshot. |
| `ModernButton` | FlatStyle + hover rengi. |
| `NoScrollPanel` | `CreateParams`: `WS_VSCROLL\|WS_HSCROLL` kaldırır. `WndProc`: `WM_HSCROLL/WM_VSCROLL` bloklar. **Sonuç: native scrollbar hiç görünmez, flicker yok.** |

### 6.3 Scroll Sistemi

```
NoScrollPanel (AutoScroll=true) → scroll pozisyonu tracked, scrollbar yok
  └─ ApplyCustomScroll(p):
      ├─ thumb Panel (5px, koyu mavi, sağ kenar)
      ├─ Timer 80ms → thumb.Top hesapla, BringToFront
      ├─ thumb drag → AutoScrollPosition güncelle
      └─ MouseWheel → AutoScrollPosition güncelle (sınır korumalı)
```

> **Neden `ShowScrollBar()` değil?**  
> `ShowScrollBar` her WinForms layout'unda sıfırlanır → timer'la flicker loop. `CreateParams` override Win32 seviyesinde çalışır, WinForms bunu geri açamaz.

---

## 7. Models.cs

### AppConfig Alanları

| Alan | Varsayılan | Açıklama |
|------|-----------|----------|
| `ArduinoPort` | `"COM4"` | Serial port |
| `ArduinoBaud` | `115200` | Baud rate |
| `TruenasIp` | `""` | NAS host — SSL bypass bu IP'e |
| `TruenasApiKey` | `""` | Bearer token |
| `EnableNasModule` | `true` | NAS polling |
| `EnableArduinoModule` | `true` | Serial sync |
| `Last*` alanlar | 0 | Startup cache — UI hemen dolu görünür |
| `LastNasServicesJ` | JArray | Servis ID lookup için cache |
| `_extra` | IDictionary | `[JsonExtensionData]` — eski alanları yok sayar |
| `LcdIntervalMs` / `PcIntervalMs` / `NasIntervalMs` / `ConfigFlushIntervalMs` | 200 / 1000 / 5000 / 30000 | Dört timer'ın periyodu (Faz 1, A8) — bkz. §3.2 |
| `SourcePath` | exe yanındaki `config.json` | `[JsonIgnore]`, `Save()`'in yazdığı gerçek yol |
| `SyncRoot` | `new object()` | `[JsonIgnore]`, `Save()` serileştirmesi ve arka plan telemetri yazımları aynı kilidi paylaşır (Faz 1, A4) |
| `MarkDirty()` / `FlushIfDirty()` | — | Kirli bayrak — her `Last*` yazımı diske inmez, `flushTimer` tetiklediğinde iner |

`Save()` artık `File.Replace` ile atomik yazıyor (`KontroXXL.Core.Configuration.JsonFileStore.WriteAtomic`) — yazım ortasında çökmede `config.json` yarım kalmaz (A4).

---

## 8. Bağımlılıklar

| Paket | Versiyon | Kullanım |
|-------|----------|----------|
| `AudioSwitcher.AudioApi.CoreAudio` | 3.0.3 | Volüm okuma/yazma |
| `Newtonsoft.Json` | 13.0.4 | TrueNAS JSON parse, config |
| `System.IO.Ports` | 10.0.3 | Arduino serial (`SerialLink`, yalnızca `KontroXXL_WinApp`'te) |
| `System.Management` | 10.0.3 | WMI (CPU hızı, PC sıcaklık, Arduino port algılama) |
| `KontroXXL.Core` (proje referansı) | — | LCD/log/config saf mantığı — bkz. §5.3 |

---

## 9. Yeni Özellik Eklemek

### Yeni LCD Sayfası

```csharp
// TrayApplicationContext.cs → UpdateLCD() → LcdMode.Home case
else if (lcdPage == N) {
    string left = "SOL KISIM";
    string right = "SAĞ";
    l0 = left + right.PadLeft(16 - left.Length);
    l1 = "İKİNCİ SATIR    ";
}
```

### Yeni TrueNAS Endpoint

```csharp
// GetTruenasData() içinde Task.WhenAll'a ekle:
var yeniTask = SafeGetString($"https://{ip}/api/v2.0/yeniEndpoint");
await Task.WhenAll(..., yeniTask);
var raw = await yeniTask;
var data = JArray.Parse(raw ?? "[]");
config.LastYeniField = ...; // Models.cs'e alan ekle

// MainForm.UpdateNasStats() içinde UI'ya yansıt
```

### Yeni Donut

```csharp
// SetupDashboardTab():
var dntYeni = new DonutProgress() {
    Title = "BAŞLIK", Unit = "%", ProgressColor = Color.HotPink
};
fDonuts.Controls.Add(dntYeni);

// UpdatePcStats():
dntYeni.Value = someValue; dntYeni.Invalidate();
```

### Yeni Arduino Komutu

```cpp
// handleCommand() içine ekle:
else if (strncmp(cmd, "XX=", 3) == 0) {
    const char* payload = cmd + 3;
    // işle
}
```

---

## 10. Bilinen Kısıtlamalar

| Konu | Detay |
|------|-------|
| GPU bilgisi | `nvidia-smi` PATH'te olmalı — AMD desteklenmiyor |
| PC sıcaklık | WMI bazı anakartlarda 0 döner |
| TrueNAS ağ arayüzü | `enp3s0 → en7x → eno1 → eth0` sırası — otomatik keşif yok |
| AudioSwitcher | .NET 4.x paketi (NU1701 suppressed), çalışıyor |
| Single instance | Mutex ile — process kill sonrası Task Manager'dan temizle |
| Dashboard boyutu | Sabit `1000×680`, yeniden boyutlandırılamaz (`MainForm.cs`) — Faz 4 (Avalonia) konusu |
| Config konumu | `config.json` hâlâ exe'nin yanında, düz metin API anahtarıyla — Faz 2'de `%APPDATA%` + DPAPI'ye taşınacak |

---

## 11. Sorun Giderme

| Belirti | Neden | Çözüm |
|---------|-------|-------|
| LCD karışık / boş | Eski firmware (String heap frag) | .ino'yu yeniden yükle |
| TrueNAS gelmiyor | Yanlış IP veya API key | Settings tab → kaydet |
| GPU 0% | nvidia-smi yok | PATH'e ekle |
| Uygulama açılmıyor | Mutex sıkışması | Task Manager → exe öldür |
| COM port bağlanmıyor | Başka uygulama tutuyor | Arduino IDE / PuTTY kapat |
| Arduino kabloyu çektim, geri takınca LCD gelmiyor | *(beklenmez — `SerialLink` 2 sn'de bir yeniden dener, A2)* | Yine de olursa uygulamayı yeniden başlat, `app.log`'da `Seri baglanti koptu` satırını ara |
