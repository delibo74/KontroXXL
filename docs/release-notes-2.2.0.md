# KontroXXL 2.2.0

İlk yayınlanan sürüm. Faz 1 (sağlamlaştırma), Faz 2 (kurulum/güncelleme zinciri) ve
Faz 4'ün ilk iki maddesini içerir.

## Yeni

- **NAS uyarıları artık Windows tepsi balonu olarak da geliyor.** Karar mantığı
  kimlik tabanlı: kapanıp yeniden açılan bir alarm yeni sayılır, aynı alarm için
  bildirim yağmuru olmaz, uygulama açılışında zaten var olan alarmlar için sahte
  bildirim basılmaz. `level` alanı okunuyor; varsayılan olarak WARNING ve üstü
  bildirilir. Ayarlar → "NAS uyarılarını bildir (tepsi balonu)" ile kapatılabilir.
  Balona tıklamak NAS Dashboard sekmesini açar.
- **Pencere artık yeniden boyutlandırılabilir.** Kenar ve köşe tutamakları, başlık
  çubuğunda Büyüt/Geri Al düğmesi, başlığa çift tıklama. En küçük boyut eskiden
  sabit olan 1000x680; pencere büyüdükçe ağ grafikleri, havuz/uyarı/servis listeleri
  ve NAS özet paneli birlikte büyür. Görünüm 1000x680'de birebir aynı kalır.

## Düzeltmeler (Faz 1 ve Faz 2'den taşınan)

- API anahtarı diske DPAPI ile şifreli yazılıyor; düz metin yalnızca bellekte.
- Yapılandırma dosyası `%APPDATA%\KontroXXL` altına taşındı; bozuk/okunamayan
  dosya artık kullanıcının ayarlarını sessizce silmiyor.
- Seri port otomatik algılama; "COM4 = otomatik" sihirli değeri kaldırıldı.
- Loglar `%APPDATA%\KontroXXL\app.log` altında, dönen dosyalarla.
- Güncelleme akışı: tepsi menüsünde "Güncellemeleri Denetle".

## Kurulum

`KontroXXL-win-Setup.exe` indirin ve çalıştırın. Paket **imzasız** olduğu için
Windows SmartScreen bir uyarı gösterir; "Daha fazla bilgi" → "Yine de çalıştır".
.NET 8 Desktop Runtime yoksa kurulum onu da getirir.

## Bilinen sınırlar

- Paket imzasız (kod imzalama sertifikası yok).
- Yüksek DPI ölçekleme henüz ayarlanmadı.
