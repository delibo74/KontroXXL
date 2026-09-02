# KontroXXL Faz 2 — Kurulum ve Güvenlik (Şartname)

> **Tarih:** 2026-09-02
> **Durum:** Onaylandı (Karaduman)
> **Önceki faz:** Faz 1 tamamlandı — `v2.1.0`, 18 commit, 151 test, Release 0 uyarı
> **Ana şartname:** `docs/superpowers/specs/2026-09-01-kontroxxl-v3.md` (Faz 2 kapsamı §5'te tanımlı)

---

## 1. Amaç

Faz 1 uygulamayı ayakta tuttu. Faz 2 onu **dağıtılabilir** yapar: çift tıkla kurulan,
kendini güncelleyen, sırrını düz metin tutmayan, Arduino'yu da kendisi programlayan
bir paket.

Bugün "kurulum" bir klasörü kopyalamaktan ibaret ve uygulama yazılabilir durumunu
exe'nin yanına yazdığı için `Program Files`'a ya da Velopack'in `current/` klasörüne
kurulamıyor.

---

## 2. Bu fazda kapatılan kusurlar

Ana şartname §3.1'den devralınanlar:

| # | Kusur | Kanıt |
|---|---|---|
| **A5** | TrueNAS API anahtarı düz metin, exe'nin yanında | `config.json:5` |
| **A6** | Yazılabilir durum kurulum dizininde — `Program Files`'a veya Velopack'in değişen `current/` klasörüne kurulunca kaybolur/patlar | `Models.cs` `Load`/`Save`, `TrayApplicationContext` log yolu |
| **A9** | `"COM4"` sihirli değeri "otomatik algıla" anlamına geliyor; gerçekten COM4'teki cihazı olan kullanıcı sürekli eziliyor | `SerialLink.ResolvePort` + `AppConfig.ArduinoPort` varsayılanı |

Faz 1'den devredilen açık maddeler:

| # | Madde | Kaynak |
|---|---|---|
| **D1** | `Save()` içinde `_dirty`, kilitsiz disk I/O'dan SONRA ayrı bir kilit alımında temizleniyor; o pencerede gelen `MarkDirty()` eziliyor | Faz 1 final fix dalgasının kendi ürettiği regresyon, park edildi |
| **D2** | `File.Replace` kaynak ve hedefin aynı volume'de olmasını gerektiriyor — config `%APPDATA%`'ya taşınırken doğrulanmalı | Task 9 review ⚠️ |
| **D3** | `Keeps_logging_after_a_rotation_whose_reopen_failed` testi reopen hatasını hiç tetiklemiyor; Task 3'ün Important #1'i **korumasız** | Final review I-2 |
| **D4** | Etkileşimli `Save()` hataları kullanıcıya görünmüyor — `Program.cs`'in `ThreadException` handler'ı `crash.log` yazıp hiçbir şey göstermiyor, üstelik A1'in kanıtı olan `crash.log`'u eziyor | Final review I-4 |

---

## 3. Onaylanan kararlar

| Konu | Karar | Gerekçe |
|---|---|---|
| Paketleme | **Velopack** | Kurulum **ve** delta otomatik güncelleme tek araçta |
| Güncelleme kaynağı | **GitHub Releases** | Her yerden güncelleme; `gh` CLI kurulu ve `delibo74` olarak yetkili |
| Depo görünürlüğü | **Kullanıcı onayıyla belirlenecek** | Dışarı açılan bir işlem; sessizce yapılmaz (§7) |
| Kurulum kapsamı | **Kullanıcı bazlı** (`%LOCALAPPDATA%`), makine geneli değil | UAC istemi hiç çıkmaz; tray uygulaması için doğru varsayılan |
| Arduino kartı | **Uno** → `-c arduino -b 115200` | Karaduman'ın kararı |
| Firmware `.hex` | **Elle bir kez export edilip depoya girer** | Firmware bu fazda değişmiyor; arduino-cli kurulumu gereksiz hareketli parça |
| avrdude | **Arduino IDE'nin paketlediği sürüm kopyalanır** | `%LOCALAPPDATA%\Arduino15\packages\arduino\tools\avrdude\8.0.0-arduino1` zaten kurulu |
| Kod imzalama | **Yok** | Sertifika ücretli; SmartScreen uyarısı kabul ediliyor |
| .NET | **net8.0 / net8.0-windows** | Makinede yalnızca SDK 8.0.301 |

### 3.1 Kasıtlı olarak YAPILMAYACAKLAR

- Makine geneli (`Program Files`) kurulum yapılmaz — kullanıcı bazlı kurulum UAC'yi tamamen atlıyor.
- Kod imzalama sertifikası alınmaz.
- `arduino-cli` kurulmaz; firmware derleme zinciri bu fazın kapsamı dışında.
- Firmware'in kendisi **değiştirilmez** — yalnızca derlenmiş `.hex` paketlenir ve yüklenir.
- Çoklu NAS (`Truenas2*`) canlandırılmaz; `config.json`'daki ölü alanlar bu fazda temizlenir.

---

## 4. Dosya yolları — bu fazın kalbi

Velopack uygulamayı `%LOCALAPPDATA%\KontroXXL\current\` altına kurar ve **her
güncellemede bu klasörü değiştirir**. Yazılabilir hiçbir şey oraya konamaz.

| İçerik | Yol | Not |
|---|---|---|
| Yapılandırma | `%APPDATA%\KontroXXL\config.json` | Roaming; Velopack'in `%LOCALAPPDATA%\KontroXXL\` kurulum köküyle çakışmaz |
| Loglar | `%APPDATA%\KontroXXL\logs\app.log` (+ `app.1.log` … `app.3.log`) | |
| Karantina | `%APPDATA%\KontroXXL\config.json.corrupt-<tarih>` | Faz 1'de eklenen C-1 mekanizması |
| Firmware | `<kurulum>\firmware\arduino_kontrol.ino.hex` | Salt okunur, güncellemeyle değişir |
| avrdude | `<kurulum>\tools\avrdude\` | Salt okunur |

### 4.1 Göç (tek seferlik, idempotent)

İlk açılışta, hedefte `config.json` **yoksa** ve exe'nin yanında **varsa**, kopyalanır.
Kopyalama sonrası `SchemaVersion: 3` yazılır. Eski dosya **silinmez** — geri dönüş yolu açık kalır.

Göç, Faz 1'de eklenen `LoadFailed` mantığından **önce** çalışır: bozuk bir eski dosya
göç edilmez, karantinaya alınır ve kullanıcı varsayılanlarla başlar.

---

## 5. Sır yönetimi (A5)

- `AppConfig.TruenasApiKey` diske **düz metin yazılmaz**.
- Yeni alan `TruenasApiKeyProtected` (base64) DPAPI `CurrentUser` kapsamıyla şifrelenir.
- Açılışta eski düz metin alan doluysa: şifrelenir, düz metin alan boşaltılır, dosya yeniden yazılır.
- Şifre çözme başarısız olursa (başka kullanıcı profili, makine değişikliği) anahtar boş kabul edilir
  ve kullanıcıya Ayarlar'da yeniden girmesi bildirilir — sessizce başarısız olunmaz.
- `ISecretProtector` Core'da arayüz, `DpapiSecretProtector` Windows projesinde. Testler
  `PlaintextSecretProtector` ile koşar.

**Not:** Faz 1'de kullanıcıya iptal etmesi söylenen anahtar hâlâ `config.json`'da düz
metin duruyorsa, göç onu şifreler ama **sızmış anahtarı şifrelemek işe yaramaz** —
iptal edilmiş olması gerekir.

---

## 6. Firmware yükleme

- Kurulum paketi `firmware/arduino_kontrol.ino.hex` ve `tools/avrdude/` taşır.
- Ayarlar sekmesinde **"Arduino'yu Programla"** düğmesi.
- Akış: portu doğrula → kullanıcıdan onay al → `SerialLink`'i durdur → avrdude çalıştır →
  çıktıyı göster → `SerialLink`'i yeniden başlat.
- **Güvenlik:** flash'tan önce hedef portun gerçekten Arduino/CH340/CP210x olduğu
  WMI ile doğrulanır. Yanlış porta yazmak başka bir cihazı bozabilir.
- avrdude komutu (Uno):
  `avrdude -C <conf> -p atmega328p -c arduino -P <COM> -b 115200 -D -U flash:w:<hex>:i`

---

## 7. GitHub deposu — dışarı açılan işlem

Depo bugün yok. Velopack'in güncelleme akışı için gerekli.

**Bu adım kullanıcının açık onayı olmadan yapılmaz.** Onay öncesi zorunlu:

1. Tüm git geçmişinde sır taraması — `config.json`, `*.log`, `Release_v2/`, API anahtarı
   deseni. Faz 1 boyunca hiçbiri commit edilmedi, ama **push öncesi doğrulanır, varsayılmaz**.
2. Depo görünürlüğü (public/private) kullanıcıya sorulur.
3. Depo adı kullanıcıya sorulur.

Push işlemi ve ilk release yayını ayrı ayrı onaylanır.

---

## 8. Kabul kriterleri

1. `dotnet build KontroXXL.sln -c Release` sıfır uyarı-hatasıyla geçer.
2. `dotnet test` yeşil.
3. Temiz bir makinede (veya temiz bir kullanıcı profilinde) kurulum `.exe`'si çift tıkla kurar,
   Başlat menüsüne kısayol koyar, uygulama açılır.
4. `%APPDATA%\KontroXXL\config.json` oluşur; kurulum dizininde **hiçbir** yazma denemesi olmaz.
5. Eski `Release_v2/config.json` mevcutken kurulum yapılınca ayarlar ve kısayollar göç eder.
6. `config.json` içinde API anahtarı **düz metin geçmez** (dosyayı açıp gözle doğrulanır).
7. Ayarlar → "Arduino'yu Programla" LCD firmware'ini yükler, sonrasında LCD normal çalışır.
8. Kaldırma (uninstall) uygulamayı ve Başlat menüsü kısayolunu temizler; `%APPDATA%\KontroXXL\`
   **kalır** (kullanıcı verisi kasten silinmez).
9. Sürüm numarası tek: `Directory.Build.props`, kurulum paketi, Ayarlar'daki "Hakkında" aynı değeri gösterir.

**3, 4, 5, 7, 8 yalnızca gerçek kurulumla doğrulanabilir ve Karaduman'a borçludur** —
Faz 1'de olduğu gibi, çalışan eski instance'ın kapatılması gerekir.

---

## 9. Bilinen riskler

| Risk | Etki | Azaltma |
|---|---|---|
| `VelopackApp.Build().Run()` mutex'ten önce çalışmalı; yanlış sırada kurulum hook'ları çalışmaz | Kurulum/güncelleme sessizce bozulur | `Program.Main`'in **ilk** satırı olacak, review'da özellikle kontrol edilecek |
| DPAPI anahtarı kullanıcı profiline bağlı | Profil/makine değişince anahtar çözülemez | Sessiz başarısızlık yasak; kullanıcıya Ayarlar'da bildirilir |
| avrdude yanlış porta yazar | Başka cihaz bozulabilir | WMI doğrulaması + kullanıcı onayı zorunlu |
| Göç kodu her açılışta koşar | Kullanıcının yeni ayarları eski dosyayla ezilir | Yalnızca hedef **yokken** kopyalar; idempotent |
| SmartScreen imzasız exe'yi uyarır | Kullanıcı korkar | Beklenen; README'de açıklanır |
| GitHub'a sır sızması | Geri alınamaz | §7'deki tarama, onaydan önce ve zorunlu |
