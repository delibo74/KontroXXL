# HANDOFF — worker-naslcd-faz2 (Dwight)

**Dal:** agent/worker-naslcd-faz2 (faz2-kurulum üzerinden). **Remote push YOK** — entegratör god.

## Durum
Faz 2 planı: Task 1–3 önceden tamamlandı (repo geçmişinde). Bu oturumda **Task 4 (sürümleme
+ yayın profili) tamamlandı**. Test: 179/179 yeşil (Task 4 öncesi 167, +12 yeni). Release
derlemesi 0 uyarı / 0 hata (spec §8.1).

## Bu oturumda yapılanlar (Task 4)
- `Directory.Build.props`: `<Version>2.2.0</Version>`; `AssemblyVersion`/`FileVersion`/
  `InformationalVersion` artık `$(Version)`'a bağlı — sürümün tek kaynağı burası.
- `src/KontroXXL.Core/Diagnostics/VersionText.cs` (YENİ): gösterilecek sürüm metnini üretir;
  `+commit` yapı üstverisini atar, ön-sürüm ekini korur, `2.2.0.0` yerine `2.2.0` verir
  (§8.9 tek-sürüm kriteri). 12 test.
- `src/KontroXXL_WinApp/MainForm.cs`: Ayarlar sekmesine "— Hakkında —" bölümü +
  `AppVersionText` (assembly damgasından okur, elle yazılmış sabit yok).
  Not: `using System.Reflection;` EKLENMEZ — .NET 8'de `System.Reflection.MethodInvoker`
  WinForms'unkiyle çakışıyor; tam nitelenmiş çağrı kullanıldı.
- `installer/publish.ps1` (YENİ): framework-dependent win-x64 yayın; publish sonrası
  exe'nin FileVersion damgasını `Directory.Build.props` ile karşılaştırıp uyuşmazsa hata verir.
  Çalıştırıldı: `publish/KontroXXL_WinApp.exe -> 2.2.0`. `publish/` gitignore'da.

## Bu oturumda yapılanlar (Task 5 — Velopack)
- `Velopack 1.2.0` paketi eklendi.
- `Program.Main`: `VelopackApp.Build().Run()` artık **ilk iş**. Mutex alan-başlatıcıdan
  Main gövdesine taşındı — statik alan başlatıcıları Main'den ÖNCE koşar, o yüzden eski
  hâlde Velopack gerçekte ilk değildi (spec §9 riski). Hook hatası yutulmuyor: crash.log'a
  yazılıp yeniden fırlatılıyor. `vpk pack` çıktısı bunu doğruladı:
  "Verified VelopackApp.Run() in ... Program::Main()".
- Tray menüsüne "Güncellemeleri Denetle" + `CheckUpdatesAsync()`: `UpdateFeedUrl` boşken
  açıkça "kaynak yapılandırılmamış" der; `mgr.IsInstalled` değilse uyarır (kurulmamış
  kopyada `ApplyUpdatesAndRestart` fırlatırdı); çift tıklamaya karşı `updateCheckRunning`
  bayrağı; restart öncesi `FlushIfDirty` + `SendGoodbye` + `serial.Dispose()` (COM portu
  bırakılmazsa yeni process porta bağlanamaz); hata sessiz kalmıyor, kullanıcıya gösteriliyor.
- **Plan düzeltmesi:** plandaki `mgr.ApplyUpdatesAndRestart(newVer)` Velopack 1.2.0'da
  DERLENMEZ — imza `ApplyUpdatesAndRestart(VelopackAsset, string[])`. `newVer.TargetFullRelease`
  geçiliyor.
- `installer/pack.ps1` (YENİ): `vpk` yoksa net hata; publish.ps1'i çağırır; sürümü aynı tek
  kaynaktan okur; **`--framework net8.0-x64-desktop`** eklendi (plan bunu atlamıştı — yayın
  framework-dependent, temiz makinede .NET 8 Desktop runtime yoksa kurulum açılmayan bir
  uygulama bırakırdı, spec §8.3). Çalıştırıldı → `releases/KontroXXL-win-Setup.exe` (7.2 MB),
  `KontroXXL-win-Portable.zip`, `KontroXXL-2.2.0-full.nupkg`. `releases/` gitignore'a eklendi.
- Uyarı: paket **imzasız** (vpk "No signing parameters" diyor) — SmartScreen uyarısı beklenen,
  spec §9'da kabul edilmiş.

## Bu oturumda yapılanlar (Task 8 — dokümanlar ve kabul)
- `README.md`: "Kurulum" bölümü (Setup exe, SmartScreen, runtime bootstrap, kaldırmada
  `%APPDATA%` kalır); "Yapılandırma" bölümü artık gerçeği anlatıyor (%APPDATA%, tek seferlik
  göç, DPAPI + çözülemezse uyarı); test sayısı 179; yapı tablosuna `installer/`.
- `DOCS.md`: §2 dosya haritası güncel (installer/, DpapiSecretProtector, Core'da
  Security//Diagnostics/, 179 test); "config exe'nin yanında" notu Task 1'den beri bayattı,
  düzeltildi; §10'daki düz-metin-anahtar kısıtı yerine gerçek ikisi (DPAPI profil-bağlı,
  paket imzasız); **YENİ §12 Kurulum ve Güncelleme** (publish/pack, tek sürüm kaynağı,
  Velopack çağrı sırası ve mutex'in neden taşındığı, güncelleme akışı, kaldırma).
- `TODO.md`: Config Şifreleme + Versiyon Kontrolü işaretlendi, başlık 2.2.0, log yolu düzeltildi.
- **YENİ** `docs/superpowers/plans/2026-09-03-faz2-kabul.md`: spec §8'in 9 kriteri tek tek.
  3 geçti (Release 0/0, 179/179, tek sürüm), 3'ü kod/test düzeyinde doğrulandı ama GERÇEK
  KURULUMLA DEĞİL (öyle işaretlendi, "geçti" denmedi), 3'ü borçlu/blokajlı. Sahte onay yok.
- Plan dosyasındaki kutucuklar: Task 1–5 ve 8 işaretlendi; Task 6–7 açık bırakıldı.

## Review turu 1 — Oscar'ın Task 4/5/8 bulguları kapatıldı (M1, M2, L1–L4)
Üç commit: `81945fc` (M1+L2), `df25e9a` (M2+L3+L4), `00f609f` (L1).
Release derlemesi **0 uyarı / 0 hata**, testler **186/186** (179 + 7 yeni).

- **M1 — `TrayApplicationContext.cs` (CheckUpdatesAsync).** Yıkım
  (`FlushIfDirty` → `SendGoodbye` → `serial.Dispose` → `trayIcon.Visible=false`)
  `ApplyUpdatesAndRestart`'tan önce yapılıyor; o çağrı fırlarsa eski `catch` yalnızca
  uyarı gösterip çıkıyor, süreç **yaşamaya devam** ediyordu: tepsi gizli, seri port
  kapalı, LCD'de "BYE BYE", timer'lar tikliyor, kullanıcı menüye ulaşamıyor.
  Artık `updateTornDown` bayrağı yıkımdan hemen önce set ediliyor; yıkım sonrası hatada
  tepsi ikonu geri gösteriliyor, "kapatılıyor" deniyor ve `Application.Exit()` çağrılıyor.
  Karar mantığı WinForms'tan ayrıldı: **YENİ** `KontroXXL.Core/Diagnostics/UpdateFailurePolicy.cs`
  + 7 test (yıkım öncesi/sonrası, boş hata metni, başlıkların ayırt edilebilirliği).
- **M2 — `installer/pack.ps1`.** `dotnet tool install -g vpk` sürüm sabitlemiyordu; temiz
  makinede csproj'daki Velopack 1.2.0 ile uyuşmayan bir vpk inebilir. Sürüm artık csproj
  `PackageReference`'tan okunuyor, hata mesajı `--version 1.2.0` ile veriliyor ve **kurulu
  vpk sürümü kütüphaneyle karşılaştırılıyor** (`publish.ps1` damga kapısının eşdeğeri,
  publish çalışmadan önce).
- **L1 — `Program.cs`.** `UnhandledException` + `ThreadException` abonelikleri Velopack
  hook'unun **üstüne** taşındı; hook fırlarsa artık ham .NET çökme diyaloğu değil uygulamanın
  kendi kutusu çıkıyor. Sıra bozulmuyor: olaya abone olmak statik başlatıcı tetiklemez,
  mutex hâlâ `Run()` sonrası alınıyor.
- **L2 — `TrayApplicationContext.cs`.** `if (updateCheckRunning) return;` sessizdi; kısa bir
  "Güncelleme denetimi zaten sürüyor." mesajı eklendi (spec §9).
- **L3/L4 — yeni `installer/common.ps1`.** `.Project.PropertyGroup.Version` tek
  PropertyGroup varsayıyordu (ikincisi eklenince `Set-StrictMode -Version Latest` altında
  fırlıyor) → `SelectSingleNode` + `^\d+\.\d+\.\d+` biçim kapısı. Damga kapısı
  `StartsWith` idi ("2.2.0" öneki "2.2.01"i geçirirdi) → iki taraf da dört parçaya
  normalize edilip eşitlik aranıyor. Ortak okuma tek dosyada toplandı.

**Doğrulama:** üç `.ps1` PowerShell ayrıştırıcısından temiz geçti; yardımcılar canlı
çalıştırıldı (props → 2.2.0, Velopack → 1.2.0, `2.2.0 == 2.2.0.0`, `2.2.0 != 2.2.01`,
geçersiz metin → `$null`); iki PropertyGroup'lu bir dosyada yeni okuma 3.1.4 verirken eski
yol StrictMode altında fırlattı. `vpk pack` bu turda YENİDEN ÇALIŞTIRILMADI (kurulu vpk
sürümü zaten 1.2.0; paket üretimi Task 5'te doğrulanmıştı).

## Durum özeti
Faz 2'de **6/8 task tamam** (1,2,3,4,5,8). Kalan 6 ve 7 blokajlı (aşağıda).
Release derlemesi 0 uyarı/0 hata, testler 186/186.
`installer/pack.ps1` → `releases/KontroXXL-win-Setup.exe` (2.2.0) üretiliyor, imzasız.

## Sıradaki adım
Blokajlar açılırsa Task 6 (firmware) → Task 7 (depo + `UpdateFeedUrl` + release).
Gerçek kurulum/kaldırma doğrulaması (spec §8.3/8.4/8.5/8.6/8.8) Karaduman'a borçlu;
bu makinede kurulum YAPILMADI, yalnızca paket üretildi.

## Blokajlar
- Task 6: Karaduman'dan `.hex` bekliyor. AYRICA `.gitignore`'da `*.hex` var → plan Task 6
  ile çelişiyor, karar gerek (`!firmware/*.hex` istisnası mı, repo dışı mı?).
- Task 7: iki onay kapısı (depo adı/görünürlük, push) + remote push bana kapalı (entegratör god).
  `UpdateFeedUrl` boş kaldığı sürece "Güncellemeleri Denetle" yalnızca bilgi mesajı verir.

## Faz 4 turu — plan + Task 7'nin `UpdateFeedUrl` parcasi (2026-09-04)

- `TrayApplicationContext.cs:238` — `UpdateFeedUrl` artik
  `https://github.com/delibo74/KontroXXL`. Depo PUBLIC oldugu icin Task 7'nin bu
  parcasi kapandi. Yorumda **Velopack GithubSource'un DEPO adresi istedigi** yaziyor:
  `.../releases` ya da `.git` ekli bir deger API tabanini bozar, her denetim 404 doner.
  Bos-deger korumasi kaldirilmadi. Release derlemesi 0/0, testler **186/186**.
- **YENI `PLAN-faz4.md`** — Faz 4 plani. Kesiften yazildi, her iddia `dosya:satir`
  ile: F4-1 tray balonu (Core'da test edilebilir `AlertNotificationPolicy`; mevcut
  tetigin uc zayif noktasi belgelendi), F4-2 resize (D1), F4-3 ilk release + kurulum
  dogrulamasi, F4-4 firmware `.hex` (D2).
- `installer/pack.ps1` bu turda **canli calistirildi**; `releases/` cikti listesi
  plandaki tabloya olcumle girdi (Setup.exe 7.2 MB, full.nupkg, `releases.win.json`,
  Portable.zip, `RELEASES`, `assets.win.json`). Release akisi `PLAN-faz4.md` §5.

### god'a birakilan kararlar
- **D1 (en kritik):** Dashboard resize — (i) Avalonia gecisi ~2-3 hafta / yuksek risk /
  gorsel kimlik yeniden uretilir / **Avalonia'da tray balonu yok, F4-1'e ek is cikarir**,
  (ii) WinForms hafif fix A+B ~2 gun / dusuk-orta risk / gorunum degismez / buyuk
  pencerede sagda-altta bosluk kalir, yuksek DPI cozulmez. Oneri: **(ii)**.
  Olcumler: MainForm'da **50 mutlak `Location`, 0 `Anchor`**; dis iskelet zaten `Dock`;
  `LineChart` olcekleniyor ama `DonutProgress` sabit koordinatla ciziyor.
- **D2:** firmware `.hex` — oneri **repo disi**, GitHub Release varligi olarak
  (`.gitignore`'daki `*.hex` kurali korunur; `.hex` turetilmis cikti, `.ino` zaten depoda).
- **D3:** ilk release simdi 2.2.0 ile cikilsin (guncelleme zincirinin canli sinavi erken olsun).

### Bu turda YAPILMAYAN (sinir)
Avalonia gecisi, resize kodu, tray balonu implementasyonu, `.gitignore` degisikligi,
GitHub release yayinlama, remote push (entegrator god).
