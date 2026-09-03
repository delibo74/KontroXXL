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

## Sıradaki adım
Task 5 (Velopack): `dotnet add package Velopack`, `VelopackApp.Build().Run()` Main'in İLK
satırı (mutex'ten önce — spec §9 riski), tray'e "Güncellemeleri Denetle", `installer/pack.ps1`.
`UpdateFeedUrl` Task 7'ye kadar boş; boşken kullanıcıya "kaynak yapılandırılmamış" demeli.

## Blokajlar (god'a bildirildi)
- Task 6: Karaduman'dan `.hex` bekliyor. AYRICA `.gitignore`'da `*.hex` var → plan Task 6
  ile çelişiyor, karar gerek (`!firmware/*.hex` istisnası mı, repo dışı mı?).
- Task 7: iki onay kapısı (depo adı/görünürlük, push) + remote push bana kapalı.
