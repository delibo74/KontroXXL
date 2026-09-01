# KontroXXL Tactical v4.6 - Durum Raporu 🚀

## 🚨 KRİTİK DÜZELTMELER (TAMAMLANDI)

- [x] **Açılmama Sorunu Çözüldü:** Uygulamanın arka planda asılı kalmasını ve port çakışmalarını önlemek için **Single Instance (Mutex)** yapısı eklendi.
- [x] **Premium Tactical UI:** Dashboard tamamen baştan tasarlandı. Kenarlıksız (borderless) form, yan menü navigasyonu, neon glow efektleri ve modern taktikal fontlar (Impact/Segoe UI Semibold) eklendi.
- [x] **Bağımsız Çalışma:** Arduino veri akışı artık Dashboard açık olmasa bile Tray üzerinden kesintisiz devam ediyor.
- [x] **Kalıcı Veri:** Kısayollar ve son bilinen NAS istatistikleri `config.json` içerisinde önbelleğe alınıyor. Açılışta doğrudan son verilerle başlanıyor.
- [x] **Türkçe Karakter Fix:** Uygulama ve kısayol isimleri LCD'ye gönderilmeden önce ASCII (İngilizce) formatına otomatik normalize ediliyor.

## ⚠️ ARDUINO & GÜÇ YÖNETİMİ

- [x] **Otomatik Kapanma/Açılma:** Bilgisayar kilitlendiğinde veya kapandığında Arduino'ya `shutdown` sinyali gönderiliyor; LCD kararıyor ve "SYSTEM OFF" moduna geçiyor.
- [x] **Bağlantı Kontrolü:** Arduino artık "ESTABLISHING..." (Bağlanıyor) modunda başlıyor ve PC'den ilk veriyi alana kadar bekliyor.
- [x] **Otomatik Dashboard:** Dashboard artık esnek (resizable) ve tam ekran uyumlu hale getirildi.

## 🛠️ GELECEK PLANLARI (TODO)

- [ ] **NAS Alert Bildirimleri:** Kritik TrueNAS alertlerini Windows tray bildirimi olarak yansıtma.
- [ ] **Config Şifreleme:** API anahtarını DPAPI ile şifreleme.
- [ ] **Versiyon Kontrolü:** Otomatik güncelleme denetimi.

---
**Teknik Not:** Uygulama `net8.0-windows` üzerinde SingleFile olarak derlendi. `app.log` dosyası üzerinden çalışma loglarını takip edebilirsiniz.
