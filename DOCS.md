# KontroXXL — Developer Documentation

> **Son Güncelleme:** 2026-03-28 | **Versiyon:** v2 | **Platform:** Windows 10/11 · .NET 8.0 · Arduino ATmega328

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

```
menu/
├── KontroXXL_WinApp/
│   ├── TrayApplicationContext.cs  ← ANA MOTOR
│   ├── MainForm.cs                ← UI
│   ├── Models.cs                  ← AppConfig, ShortcutItem
│   ├── Program.cs                 ← Entry point + mutex
│   └── KontroXXL_WinApp.csproj
├── arduino_kontrol/
│   └── arduino_kontrol.ino
├── Release_v2/                    ← Çalıştırılabilir build
│   ├── KontroXXL_WinApp.exe
│   ├── config.json                ← İlk çalıştırmada oluşur
│   └── app.log                    ← Runtime log (>1MB → rotate)
├── tools/
│   └── IconGen.cs/.csproj         ← Tek seferlik ikon aracı
└── OPTIMIZATIONS.md               ← 26/26 bulgu tamamlandı
```

---

## 3. TrayApplicationContext.cs — Ana Motor

### 3.1 Başlatma Akışı

```
Program.Main()
  └─► new TrayApplicationContext()
        ├─ AppConfig.Load()           // config.json oku
        ├─ InitLog()                  // app.log aç (>1MB → rotate to .bak)
        ├─ new CoreAudioController()  // ses cihazı
        ├─ InitSerial()               // Arduino COM port
        ├─ InitHttpClient()           // SSL bypass: sadece TruenasIp'e
        ├─ InitPerformanceCounters()  // CPU + RAM (bir kez oluşturulur)
        ├─ new MainForm()             // UI
        └─ updateTimer.Start()        // 1 sn interval
```

### 3.2 Update Döngüsü (her 1 saniye)

```
updateTimer.Tick → UpdateSystemInfo() [async Task]
  ├─ GetCpuUsage()     → PerformanceCounter (cached, Thread.Sleep yok)
  ├─ GetCpuSpeed()     → WMI MaxClockSpeed × %freq
  ├─ GetRamUsage()     → PerformanceCounter (cached)
  ├─ GetGpuInfo()      → nvidia-smi (2 sn cache — process her tick değil)
  ├─ GetPcTemp()       → WMI ThermalZone (5 sn cache)
  ├─ GetPcNetSpeed()   → NetworkInterface foreach (LINQ yok, iterator alloc yok)
  ├─ GetTruenasData()  → Task.WhenAll (6 endpoint paralel) ←── kritik
  ├─ UpdatePcStats()   → MainForm UI güncelle
  ├─ UpdateNasStats()  → MainForm UI güncelle
  └─ UpdateLCD()       → Serial komut gönder
```

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

| Komut | Açıklama | Örnek |
|-------|----------|-------|
| `L0=<16char>` | Satır 0 yaz | `L0=CPU:76%   3.60G` |
| `L1=<16char>` | Satır 1 yaz | `L1=RAM:42%   04:35` |
| `B1=<0-100>` | Bar grafik | `B1=72` |
| `PING` | Bağlantı testi | Cevap: `PONG` |

### 4.2 Arduino → PC Mesajları

| Mesaj | Tetikleyici |
|-------|-------------|
| `VOL+` | Encoder saat yönü |
| `VOL-` | Encoder ters yön |
| `BTN` | Kısa basış |
| `LONGBTN` | Uzun basış (>600ms) |

### 4.3 LCD Sayfaları (Home Mode)

| Page | Satır 0 | Satır 1 |
|------|---------|---------|
| 0 | `CPU:76%   3.60G` ← sağa yaslı frekans | `RAM:42%   04:35` ← sağa yaslı saat |
| 1 | `GPU:45% 68C` | `Fan:30% 7Mbps` |
| 2 | `NAS:55% 42C` | `↑3Mb ↓0Mb` |
| 3 | `> NAS DASHBOARD` | `2 SYSTEM ALERTS!` |

> Sağa yaslama: `PadLeft(16 - leftSide.Length)` — rakamlar değişmez, sadece boşluk eklenir.

### 4.4 LCD Modları

```csharp
enum LcdMode { Home, Menu, NasApps, NasPools }
// Home:    döngüsel sayfa (encoder → ileri/geri)
// Menu:    kısa basış açar, encoder seçim, basış onayla
// NasApps: app listesi, start/stop
// NasPools: storage pool doluluk
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

---

## 6. MainForm.cs — UI

### 6.1 Kontrol Hiyerarşisi

```
Form (960×720, FormBorderStyle.None, yuvarlak köşe)
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

---

## 8. Bağımlılıklar

| Paket | Versiyon | Kullanım |
|-------|----------|----------|
| `AudioSwitcher.AudioApi.CoreAudio` | 3.0.3 | Volüm okuma/yazma |
| `Newtonsoft.Json` | 13.0.4 | TrueNAS JSON parse, config |
| `System.IO.Ports` | 10.0.3 | Arduino serial |
| `System.Management` | 10.0.3 | WMI (CPU hızı, PC sıcaklık) |

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

---

## 11. Sorun Giderme

| Belirti | Neden | Çözüm |
|---------|-------|-------|
| LCD karışık / boş | Eski firmware (String heap frag) | .ino'yu yeniden yükle |
| TrueNAS gelmiyor | Yanlış IP veya API key | Settings tab → kaydet |
| GPU 0% | nvidia-smi yok | PATH'e ekle |
| Uygulama açılmıyor | Mutex sıkışması | Task Manager → exe öldür |
| COM port bağlanmıyor | Başka uygulama tutuyor | Arduino IDE / PuTTY kapat |
| app.log büyüklüğü | Rotate çalışmadı | Elle sil — sonraki açılışta temiz başlar |
