# KontroXXL 2.2.1

Acil düzeltme sürümü. 2.2.0 kurulu bir makinede uygulamanın **hiç açılmamasına** yol
açan bir hata giderildi.

## Düzeltmeler

- **Bozuk bir API anahtarı artık uygulamayı açılmaz hâle getirmiyor.** TrueNAS API
  anahtarı yapıştırılırken araya bir satır sonu karıştığında, HTTP `Authorization`
  başlığı kurulurken atılan istisna başlangıç akışının tamamını düşürüyordu: tepsi
  ikonu yok, LCD yok, Ayarlar'a ulaşıp değeri düzeltmek bile mümkün değildi. Artık:
  - Anahtar kaydedilirken ve okunurken **normalize ediliyor** (yapıştırmadan gelen
    satır sonu ve boşluklar temizlenir, satıra bölünmüş anahtar birleştirilir).
  - Anahtar gerçekten kullanılamaz durumdaysa **yalnızca NAS modülü** susar; tepsi
    ikonu, Arduino/LCD ve Ayarlar çalışmaya devam eder.
  - Başlangıç yine de yarıda kalırsa uygulama **güvenli moda** düşer: tepsi ikonu ile
    "Ayarları Aç" ve "Çıkış" her hâlükârda erişilebilir kalır.
- **Sessiz 401 döngüsü kalktı.** Anahtar yok ya da geçersizken NAS'a istek atılmıyor;
  durum tepsi ipucunda ve Ayarlar'da açıkça yazıyor.
- Karar mantığı Core'da test edilebilir bir politikaya taşındı (`ApiKeyPolicy`),
  14 birim testiyle: sonda satır sonu, ortada `CRLF`, boş, yalnızca boşluk, geçersiz
  karakter ve temiz anahtar.

## Yükseltme

2.2.0 kuruluysa tepsi menüsünden **"Güncellemeleri Denetle"** yeterlidir. Temiz kurulum
için `KontroXXL-win-Setup.exe`. Paket **imzasız** olduğu için SmartScreen uyarısı çıkar;
"Daha fazla bilgi" → "Yine de çalıştır".

## Bilinen sınırlar

- Paket imzasız (kod imzalama sertifikası yok).
- Yüksek DPI ölçekleme henüz ayarlanmadı.
