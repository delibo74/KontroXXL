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

## Durum özeti
Faz 2'de **6/8 task tamam** (1,2,3,4,5,8). Kalan 6 ve 7 blokajlı (aşağıda).
Release derlemesi 0 uyarı/0 hata, testler 179/179.
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
