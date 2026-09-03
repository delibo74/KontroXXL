# KontroXXL Tactical 2.2.0 - Durum Raporu 🚀

## 🚨 KRİTİK DÜZELTMELER (TAMAMLANDI)

- [x] **Açılmama Sorunu Çözüldü:** Uygulamanın arka planda asılı kalmasını ve port çakışmalarını önlemek için **Single Instance (Mutex)** yapısı eklendi.
- [x] **Premium Tactical UI:** Dashboard tamamen baştan tasarlandı. Kenarlıksız (borderless) form, yan menü navigasyonu, neon glow efektleri ve modern taktikal fontlar (Impact/Segoe UI Semibold) eklendi.
- [x] **Bağımsız Çalışma:** Arduino veri akışı artık Dashboard açık olmasa bile Tray üzerinden kesintisiz devam ediyor.
- [x] **Kalıcı Veri:** Kısayollar ve son bilinen NAS istatistikleri `config.json` içerisinde önbelleğe alınıyor. Bu önbellek yalnızca **LCD** için açılışta doğrudan kullanılıyor (`BuildViewData`); **WinForms dashboard'u** önbelleği açılışta okumuyor — donutlar ilk telemetri tick'ine kadar 0 gösteriyor (bkz. `DOCS.md` §7).
- [x] **Türkçe Karakter Fix:** Uygulama ve kısayol isimleri LCD'ye gönderilmeden önce `KontroXXL.Core.Lcd.LcdText.Sanitize` ile ASCII (İngilizce) formatına normalize ediliyor — 12 Türkçe harf birebir karşılıkla değiştiriliyor, uzunluk korunuyor.

## ⚠️ ARDUINO & GÜÇ YÖNETİMİ

- [x] **Otomatik Kapanma/Açılma:** Bilgisayar uyku moduna geçtiğinde (`PowerModeChanged`/Suspend) veya oturum kapatıldığında (`SessionEnding`, `SessionEnded`) Arduino'ya `shutdown` sinyali gönderiliyor; LCD kararıyor ve "SYSTEM OFF" moduna geçiyor. **Not:** ekran kilitlendiğinde (`SessionSwitch`/`SessionLock`) bu sinyal gönderilmiyor — o olay hiçbir yerde dinlenmiyor.
- [x] **Bağlantı Kontrolü:** Arduino artık "ESTABLISHING..." (Bağlanıyor) modunda başlıyor ve PC'den ilk veriyi alana kadar bekliyor.
- [ ] **Otomatik Dashboard:** Dashboard hâlâ esnek (resizable) değil — `MainForm.cs`'de `MinimumSize = MaximumSize = new Size(1000, 680)` pencereyi kilitliyor. Yeniden boyutlandırma/tam ekran uyumu **Faz 4 (Avalonia geçişi)** kapsamına ertelendi.

## 🛠️ GELECEK PLANLARI (TODO)

- [ ] **NAS Alert Bildirimleri (Faz 4):** Kritik TrueNAS alertlerini Windows tray bildirimi olarak yansıtma. Bugün LCD ticker'ı var (`_lcdTickerText`, yeni alarmda 10 sn kayan yazı) ama Windows tray balonu yok.
- [x] **Config Şifreleme (Faz 2 — tamamlandı):** API anahtarı DPAPI ile şifreleniyor (`TruenasApiKeyProtected`); mevcut düz metin anahtar ilk açılışta bir kez göç ettiriliyor. Çözülemezse Ayarlar'da uyarı çıkar.
- [x] **Versiyon Kontrolü (Faz 2 — tamamlandı):** Velopack entegre; tray → "Güncellemeleri Denetle", `installer/pack.ps1` kurulum paketi üretiyor. **Açık:** `UpdateFeedUrl` sabiti Task 7'de (GitHub deposu) doldurulacak; o zamana kadar menü "kaynak yapılandırılmamış" der.
- [ ] **Dashboard Yeniden Boyutlandırma (Faz 4 — Avalonia):** Yukarıdaki "Otomatik Dashboard" maddesiyle aynı iş; WinForms'ta pencere kilitli kalacak, çözüm Avalonia geçişiyle geliyor.

---
**Teknik Not:** Uygulama `net8.0-windows` üzerinde framework-bağımlı olarak derlenir (`installer/publish.ps1`) ve Velopack ile paketlenir. Loglar artık `%APPDATA%\KontroXXL\app.log` altında.
