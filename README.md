# KontroXXL

Windows PC + TrueNAS telemetrisini 16×2 I²C LCD'ye basan tray uygulaması.

## Yapı

| Klasör | İçerik |
|---|---|
| `src/KontroXXL.Core` | Platform-bağımsız saf mantık — LCD biçimlendirme, menü durum makinesi, log, config. **Windows API'si içermez.** |
| `src/KontroXXL_WinApp` | WinForms arayüzü + tray + donanım erişimi (Faz 4'te Avalonia ile değişecek) |
| `tests/KontroXXL.Core.Tests` | xUnit, 150 test, donanım gerektirmez |
| `firmware/arduino_kontrol` | ATmega328 firmware'i (`.ino`) |
| `docs/superpowers/` | Şartname ve faz planları |

## Derleme

```bash
dotnet build KontroXXL.sln -c Debug
dotnet test
dotnet run --project src/KontroXXL_WinApp
```

Gereksinim: .NET SDK 8.0 (bu depo 8.0.301 ile derlendi/test edildi)

`dotnet run` yalnızca bir örnek çalışır — uygulama mutex tabanlı single-instance korumalı; ikinci bir örnek hemen çıkar.

## Yapılandırma

`config.json` şu an exe'nin yanında oluşur (Faz 2'de `%APPDATA%\KontroXXL\` altına taşınıyor).
**API anahtarı düz metin tutuluyor — dosya `.gitignore`'da, asla commit etme.**

Dört timer periyodu (`LcdIntervalMs`, `PcIntervalMs`, `NasIntervalMs`, `ConfigFlushIntervalMs`) de bu dosyadan ayarlanır — ayrıntı için `DOCS.md` §3.2.

**Elle düzenlemeden önce uygulamayı kapat** (tray → Çıkış). Uygulama çalışırken
`flushTimer` en geç `ConfigFlushIntervalMs` (varsayılan 30 sn) içinde
`config.json`'ı kendi bellekteki haliyle üzerine yazar — dosyayı açıkken elle
değiştirirsen değişiklik birkaç saniye içinde geri alınır. Kapat, düzenle,
yeniden başlat.

## Katman kuralı

`KontroXXL.Core` şu assembly'lere referans veremez: `System.Windows.Forms`,
`System.IO.Ports`, `System.Management`, `Microsoft.Win32.Registry`,
`AudioSwitcher.*`, `Avalonia`. Kural `ArchitectureTests` ile zorlanır — önek
eşleşmesi kullanıldığı için `AudioSwitcher.AudioApi.CoreAudio` ve
`Avalonia.Controls`/`Avalonia.Base` gibi alt derlemeler de yakalanır.

## Daha fazlası

Mimari, başlatma akışı, LCD protokolü ve sorun giderme için `DOCS.md`; yapılacaklar listesi için `TODO.md`.
