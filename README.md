# KontroXXL

Windows PC + TrueNAS telemetrisini 16×2 I²C LCD'ye basan tray uygulaması.

## Yapı

| Klasör | İçerik |
|---|---|
| `src/KontroXXL.Core` | Platform-bağımsız saf mantık — LCD biçimlendirme, menü durum makinesi, log, config. **Windows API'si içermez.** |
| `src/KontroXXL_WinApp` | WinForms arayüzü + tray + donanım erişimi (Faz 4'te Avalonia ile değişecek) |
| `tests/KontroXXL.Core.Tests` | xUnit, 179 test, donanım gerektirmez |
| `firmware/arduino_kontrol` | ATmega328 firmware'i (`.ino`) |
| `installer/` | `publish.ps1` (yayın profili) + `pack.ps1` (Velopack kurulum paketi) |
| `docs/superpowers/` | Şartname ve faz planları |

## Derleme

```bash
dotnet build KontroXXL.sln -c Debug
dotnet test
dotnet run --project src/KontroXXL_WinApp
```

Gereksinim: .NET SDK 8.0 (bu depo 8.0.301 ile derlendi/test edildi)

`dotnet run` yalnızca bir örnek çalışır — uygulama mutex tabanlı single-instance korumalı; ikinci bir örnek hemen çıkar.

## Kurulum

`installer/pack.ps1` çalıştırıldığında `releases/KontroXXL-win-Setup.exe` üretilir
(Velopack). Kurulum exe'si çift tıklanır; Başlat menüsüne ve masaüstüne kısayol koyar,
uygulamayı `%LOCALAPPDATA%\KontroXXL` altına kurar.

- **SmartScreen uyarısı beklenir:** paket imzasız. "Daha fazla bilgi" → "Yine de çalıştır".
- Yayın framework-bağımlıdır; temiz bir makinede .NET 8 Desktop runtime yoksa
  kurulum bootstrapper'ı önce onu kurar (`--framework net8.0-x64-desktop`).
- **Kaldırma** uygulamayı ve kısayolları siler, `%APPDATA%\KontroXXL\` **kalır** —
  ayarların ve API anahtarın kasten silinmez.
- Güncelleme: tray → **Güncellemeleri Denetle**. Güncelleme kaynağı (`UpdateFeedUrl`)
  henüz yapılandırılmadığı için bugün yalnızca bunu söyleyen bir mesaj gösterir.

Sürüm numarasının tek kaynağı `Directory.Build.props` içindeki `<Version>`;
kurulum paketi ve Ayarlar → "Hakkında" satırı aynı değeri gösterir.

## Yapılandırma

`config.json` **`%APPDATA%\KontroXXL\config.json`** altında tutulur (Faz 2 / A6); log ve
crash dosyaları da oradadır. Exe'nin yanında eski bir `config.json` varsa ilk açılışta
bir kez göç ettirilir — hedef yoksa kopyalanır, üzerine asla yazılmaz.

**API anahtarı DPAPI ile şifreli** saklanır (`TruenasApiKeyProtected`, kullanıcı profiline
bağlı). Düz metin alan artık yazılmaz. Anahtar çözülemezse (profil/makine değişimi)
Ayarlar sekmesinde kırmızı uyarı çıkar ve yeniden girilmesi istenir — sessizce boş geçilmez.
Yine de `config.json`'ı **commit etme**, `.gitignore`'da.

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
