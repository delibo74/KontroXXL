# KontroXXL 2.2.2

Arduino bağlantısı ve sayfa yerleşimi düzeltmeleri.

## Düzeltmeler

- **Arduino bağlantısı artık kararlı.** Seri port her ~2 saniyede bir açılıp kapanıyordu
  (`app.log`'da 822 "Seri baglanti koptu" kaydı); LCD hiçbir zaman kararlı veri
  göremiyordu. Sebep okuma modeliydi: okuma bir ThreadPool devamlılık zincirinde
  yapılıyordu, Windows ise bekleyen seri okumayı onu **başlatan thread** sonlandığında
  iptal ediyor. Okuma artık ömrü boyunca yaşayan **kendi thread'inde ve senkron**.
  - Gerçek bir kopmada yeniden deneme **üstel geri çekilmeli** (2 sn → 30 sn tavan).
  - Aynı hata tekrarlarken loga **tek satır** yazılıyor; sağlıklı durumda log sessiz.
- **Sayfalar artık doğru sırada.** NAS Dashboard'da havuz/uyarı/servis bölümleri
  sayfanın üstünde, NAS özeti (donut'lar, REBOOT/SHUTDOWN) altında görünüyordu;
  NAS Apps'te "MANAGED APPLICATIONS" başlığı listenin altına düşüyordu. Sebep
  WinForms'un `Dock.Top` kontrolleri ekleme sırasının tersine yığması.
- **Sürüm rozeti gerçek sürümü gösteriyor.** Sol üstteki etiket "v2.0" olarak sabit
  yazılmıştı; artık assembly sürümünden okunuyor.

## Yükseltme

2.2.1 kuruluysa tepsi menüsünden **"Güncellemeleri Denetle"** yeterlidir.

## Bilinen sınırlar

- Paket imzasız (kod imzalama sertifikası yok).
- Yüksek DPI ölçekleme henüz ayarlanmadı.
