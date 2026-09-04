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

- [x] **NAS Alert Bildirimleri (Faz 4 — F4-1 tamamlandı):** Yeni TrueNAS uyarıları artık Windows tepsi balonu olarak da çıkıyor. Karar mantığı Core'da test edilebilir bir politikada: `AlertNotificationPolicy` (kimlik tabanlı takip, `level` eşiği, açılışta taban alma, kısma). Ayarlar → "NAS uyarılarını bildir" ile kapatılabilir; balona tıklamak NAS sekmesini açar.
- [x] **Config Şifreleme (Faz 2 — tamamlandı):** API anahtarı DPAPI ile şifreleniyor (`TruenasApiKeyProtected`); mevcut düz metin anahtar ilk açılışta bir kez göç ettiriliyor. Çözülemezse Ayarlar'da uyarı çıkar.
- [x] **Versiyon Kontrolü (Faz 2 — tamamlandı):** Velopack entegre; tray → "Güncellemeleri Denetle", `installer/pack.ps1` kurulum paketi üretiyor. `UpdateFeedUrl` Task 7'de dolduruldu. **v2.2.0 YAYINLANDI** (2026-09-04): 6 varlık, draft/prerelease **false**, depo public. Feed anonim olarak okunabiliyor — uygulamanın kendi `GithubSource` çağrısıyla doğrulandı. **Kalan:** GUI'den güncelleme tıklaması ve 2.2.1 ile ikinci tur (çalışan eski kopyanın kapatılması gerekiyor), `PLAN-faz4.md` §5.3.
- [x] **Arduino seri port döngüsü + sayfa yerleşimi (2026-09-04, v2.2.2):** Seri port her ~2 sn açılıp kopuyordu (822 kayıt) çünkü okuma ThreadPool devamlılığında yapılıyordu ve Windows bekleyen seri okumayı onu başlatan thread ölünce iptal ediyor; okuma artık kendi thread'inde ve senkron, yeniden deneme üstel geri çekilmeli (`SerialReconnectPolicy`, Core). Ölçüm: 75 sn'de 0 kopma. Ayrıca `Dock.Top` tersliği yüzünden NAS Dashboard ve NAS Apps sayfaları başaşağı görünüyordu (`DockTopStack`, Core); sürüm rozeti sabit "v2.0" yerine assembly sürümünden okunuyor.
- [x] **API anahtarı çökmesi (F4-5 — 2026-09-04, v2.2.1):** Yapıştırılan TrueNAS anahtarındaki satır sonu `AuthenticationHeaderValue`'yu fırlatıyor, istisna `TrayApplicationContext` kurucusunda atıldığı için uygulama **hiç açılmıyordu**. Anahtar artık girişte ve okumada `ApiKeyPolicy` ile normalize ediliyor; kullanılamaz anahtar yalnızca NAS modülünü susturuyor (tepsi + Arduino/LCD + Ayarlar yaşıyor), kurucu yine de düşerse güvenli mod tepsi ikonu bırakıyor. Anahtar yok/geçersizken NAS'a istek atılmıyor (sessiz 401 döngüsü kalktı); durum tepsi ipucunda ve Ayarlar'da yazılı.
- [x] **Dashboard Yeniden Boyutlandırma (Faz 4 — F4-2 tamamlandı):** D1 kararı **(ii) WinForms hafif fix** çıktı, Avalonia'ya geçilmedi. Pencere artık esniyor: kenar/köşe tutamakları (WM_NCHITTEST), Büyüt/Geri Al düğmesi, her Resize'da yeniden üretilen yuvarlak köşe. İçerik de büyüyor (ağ grafikleri, havuz/uyarı/servis listeleri, NAS özet paneli). En küçük boyut 1000x680; 1000x680'de görünüm birebir aynı.

---
**Teknik Not:** Uygulama `net8.0-windows` üzerinde framework-bağımlı olarak derlenir (`installer/publish.ps1`) ve Velopack ile paketlenir. Loglar artık `%APPDATA%\KontroXXL\app.log` altında.

**Faz 4 planı:** `PLAN-faz4.md` (öncelikler, Avalonia vs WinForms karar analizi, release akışı).
