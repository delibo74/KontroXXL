# PLAN — Faz 4 (KontroXXL 2.2.0 → 2.3.0)

**Dal:** `faz2-kurulum` · **Yazan:** Dwight · **Tarih:** 2026-09-04
**Durum:** Faz 2 kapandı (Task 7'nin `UpdateFeedUrl` parçası bu turda kapatıldı).
Bu belge **plandır** — içindeki hiçbir büyük iş bu turda başlatılmadı.

Bu plan **keşiften** yazıldı, hafızadan değil: her iddianın yanında `dosya:satır` var.
Üç karar (D1, D2, D3) god'a/kullanıcıya bırakılıyor; gerekçeler aşağıda.

---

## 0. Bu turda gerçekten yapılanlar

| İş | Sonuç |
|---|---|
| `UpdateFeedUrl` dolduruldu (Task 7 kalanı) | `TrayApplicationContext.cs:238` → `https://github.com/delibo74/KontroXXL` · commit `7fa8a56` |
| Release derlemesi | 0 uyarı / 0 hata |
| Testler | 186/186 yeşil |
| `installer/pack.ps1` canlı çalıştırıldı | `releases/` gerçekten üretildi (§5.1'deki varlık listesi tahmin değil, ölçüm) |

---

## 1. Faz 4 kapsamı ve öncelik

`TODO.md` "GELECEK PLANLARI" iki açık kalem bırakıyor. Öncelik sırası ve gerekçesi:

| # | Kalem | Öncelik | Neden bu sırada |
|---|---|---|---|
| ~~F4-1~~ ✅ | NAS kritik alert → Windows tray balonu — **TAMAMLANDI** (dal `agent/worker-naslcd-faz4-tray`) | **1 (önce)** | Küçük, izole, geri alınabilir; kullanıcıya doğrudan değer; **UI çatısından bağımsız** — D1 hangi yöne giderse gitsin bu kod boşa gitmez (bkz. §2.4) |
| F4-2 | Dashboard yeniden boyutlandırma | **2 (D1 kararından sonra)** | Efor aralığı 2 güne karşı 2–3 hafta; yanlış seçim Faz 4'ü tek başına yutar |
| F4-3 | İlk gerçek GitHub release + kurulum doğrulaması | **1 ile paralel** | Velopack zinciri kod düzeyinde bitti ama **hiç gerçek kurulumla denenmedi** (spec §8.3/8.4/8.5/8.6/8.8 hâlâ borçlu, `HANDOFF.md`) |
| F4-4 | Task 6 — firmware `.hex` | **karar bekliyor** | Tek satırlık bir `.gitignore` kararı, bkz. §4 |

**Öneri:** Faz 4 = F4-1 + F4-3 + (D1 "hafif fix" çıkarsa) F4-2. D1 "Avalonia" çıkarsa
F4-2 kendi başına **Faz 5** olmalı; Faz 4'ün içine sığmaz.

---

## 2. F4-1 — NAS kritik alert → Windows tray bildirimi — ✅ TAMAMLANDI

> **Durum (2026-09-04):** uygulandı, dal `agent/worker-naslcd-faz4-tray`,
> commit'ler `4b4f3fc` (Core politikası + 31 test) ve `6fe9d4c` (tepsi/UI teli).
> §2.2'deki üç zayıf noktanın **üçü de** kapatıldı; §2.3'teki tasarım birebir
> uygulandı, tek fark: politikanın kısma penceresi alarmları **düşürmüyor**,
> biriktirip pencere dolunca tek balonda çıkarıyor. Testler 217/217 yeşil,
> Release derlemesi 0 uyarı / 0 hata. Ayar: `AppConfig.NotifyOnNasAlerts`
> (varsayılan açık), Ayarlar sekmesinde "NAS uyarılarını bildir (tepsi balonu)".


### 2.1 Bugün ne var

`TrayApplicationContext.cs:729-740`: alarm sayısı bir öncekini aştığında
`_lcdTickerText` set ediliyor, 10 sn LCD'de kayıyor (`:588-589`, `:601-602`).
Windows tarafında **hiçbir şey** yok. Kullanıcı LCD'ye bakmıyorsa alarmı kaçırır.

### 2.2 Mevcut tetiğin üç zayıf noktası

Düzeltilmeden balon eklenirse aynen miras alınır:

1. **Sayı tabanlı, kimlik tabanlı değil.** `na > _prevNasAlertCount` yalnızca toplamı
   karşılaştırıyor. Bir alarm kapanıp aynı tick'te başka biri açılırsa sayı değişmez →
   **yeni alarm sessizce kaçar**.
2. **Önem derecesi yok.** `:723` yalnızca `dismissed` alanına bakıyor; TrueNAS'ın
   `level` alanı (`INFO`/`WARNING`/`CRITICAL`) hiç okunmuyor. "Kritik alert" başlığı
   bugün gerçekte "herhangi bir alert" demek.
3. **Yeniden başlatmada sıfırlanır.** `_prevNasAlertCount` 0'dan başlıyor → uygulama
   her açılışında mevcut alarmlar "yeni" sayılır (LCD'de zararsız, **balonda gürültü**).

### 2.3 Önerilen tasarım

Bu depoda yerleşik olan deseni izle (`UpdateFailurePolicy.cs` + 7 test): **karar mantığı
WinForms'tan ayrı, Core'da ve test edilebilir.**

- **YENİ** `src/KontroXXL.Core/Diagnostics/AlertNotificationPolicy.cs` — saf fonksiyon:
  - girdi: mevcut alarmların `(id, level, title)` listesi + daha önce bildirilmiş id
    kümesi + `now`
  - çıktı: `ShouldNotify`, `Title`, `Body`, güncellenmiş id kümesi
  - kurallar: **id farkı** (sayı değil) · yalnızca `CRITICAL`/`WARNING` · aynı id için
    tekrar bildirim yok · N dakikalık kısma (alarm fırtınasında tek balon) · ilk tick'te
    "önceden var olanlar" sessiz kabul edilir (madde 3'ün çözümü)
  - `LcdText.Sanitize` **kullanılmaz** — balon Unicode taşır, Türkçe harfler korunur
    (sanitize yalnızca LCD yolunda gerekli, `TODO.md` Türkçe karakter maddesi)
- `TrayApplicationContext`: `:729`'daki blok politikayı çağırır; `true` dönerse
  `trayIcon.ShowBalloonTip(...)` **ve** mevcut LCD ticker'ı birlikte tetikler.
  `RunOnUi` zaten var, `ShowBalloonTip` UI thread'i ister.
- Balona tıklama → `mainForm` NAS sekmesini açsın (`BalloonTipClicked`).
- Ayarlar'a **açma/kapama kutusu** + `AppConfig` alanı (`NotifyOnNasAlerts`, varsayılan
  açık). Sessiz saat isteği gelirse aynı politikaya eklenir.

### 2.4 Neden bu iş D1'den önce yapılabilir

Karar mantığı Core'da; Core'un `Avalonia` referansı **zaten `ArchitectureTests.cs` ile
yasak** (`Forbidden` listesinde açıkça var). Yani politika + testleri UI çatısı değişse
de aynen kalır; yalnızca 5–10 satırlık `ShowBalloonTip` çağrısı taşınır.

**Efor:** ~0.5–1 gün (politika + 8–10 test + tray teli + ayar kutusu + doküman).
**Risk:** düşük. Tek dikkat: Win10/11'de balon toast'a yönlenir, kullanıcının
"bildirimleri kapat" ayarı bunu yutabilir — bu bizim hatamız değil, ama sessiz
başarısızlık olur; log'a "balon gönderildi" satırı düşülmeli (spec §9 ruhu).

---

## 3. D1 — **EN KRİTİK KARAR:** Dashboard yeniden boyutlandırma

### 3.1 Bugünkü kilit ve nedeni

- `MainForm.cs:264-265` → `ClientSize = MinimumSize = MaximumSize = 1000x680`
- `MainForm.cs:267` → `FormBorderStyle.None` (özel başlık çubuğu var, `:307`)
- `MainForm.cs:270` → yuvarlak köşe `Region`, **bir kez** `Width/Height`'tan üretiliyor

Kilidi kaldırmak tek satır. **Asıl iş içeriğin yeniden akması** ve orada durum şu:

| Ölçüm | Değer | Anlamı |
|---|---|---|
| `Location = new Point(...)` | **50** | mutlak konumlu kontrol |
| `Anchor` kullanımı | **0** | hiçbiri esnemez |
| `Dock` kullanımı | çok | dış iskelet (topBar/sideNav/contentContainer/sekmeler) zaten esnek |
| Sabit boyutlar | `chartPcNet 720x180` (`:549`), `chartNasNet 380x70` (`:581`), `pNet Height=240` (`:541`), etiketlerde `MaximumSize=740` (`:637,:664,:677`) | genişleyen pencerede sağda boşluk kalır |
| Ayarlar sekmesi | elle `y += 22/28/40` akışı (`:801-860`) | düzen kodu, tasarımcı değil |
| Özel çizim kontrolleri | `DonutProgress` (`:66`), `LineChart` (`:115`) | **`LineChart` ölçeklenir** (`Width`/`Height` kullanıyor, `:158-175`); **`DonutProgress` ÖLÇEKLENMEZ** — `Rectangle(20,15,100,100)` ve `y=130` sabit (`:88`, `:110`) |

### 3.2 Seçenek (i) — Tam Avalonia geçişi

**Kapsam:** `src/KontroXXL_WinApp` (MainForm 1215 + TrayApplicationContext 863 satır)
yeniden yazılır. `KontroXXL.Core` **dokunulmaz** (zaten UI-bağımsız, 186 test aynen kalır).

**Efor:** ~**2–3 hafta** (tek geliştirici). Kırılım:

- XAML düzeni + tema (neon/tactical görünümün yeniden üretimi): 5–7 gün
- `DonutProgress` + `LineChart` yeniden çizimi (GDI+ → Avalonia `DrawingContext`): 2–3 gün
- Tray: **Avalonia'nın `TrayIcon`'unda balon/toast yok.** F4-1 için Windows'a özel
  `AppNotification`/`NotifyIcon` köprüsü ya da üçüncü parti gerekir: 1–2 gün + risk
- Velopack yeniden telleme + `pack.ps1` hedefi + gerçek kurulum testi: 1–2 gün
- Ayarlar/NAS/Kısayol sekmelerinin davranış eşitliği ve regresyon avı: 4–5 gün

**Risk: yüksek.**

- Faz 2'de kazanılan her şey (Velopack çağrı sırası, mutex konumu, DPAPI akışı,
  `UpdateFailurePolicy` teli) **yeniden doğrulanmalı** — `DOCS.md §12.3`'teki
  "`VelopackApp.Run()` ilk iş" kuralı yeni `Program.cs`'te kolayca bozulur.
- `System.IO.Ports`, `System.Management`, `AudioSwitcher` telleri WinForms'a değil
  Windows'a bağlı — taşınır, ama her biri yeniden test edilmeli.
- Faz 4'ün diğer kalemleri bu süre boyunca **donar**.

**UI tutarlılığı: uzun vadede en iyi, kısa vadede en kötü.** Avalonia'da düzen gerçekten
esnek olur (Grid/`*` boyutlar, DPI ölçekleme bedava). Ama geçiş sırasında "aynı görünüyor
ama biraz farklı" bir dönem kaçınılmaz: fontlar (`Impact`, `Segoe UI Semibold`) ve
piksel-hassas neon efektleri birebir eşleşmez.

**Ne zaman doğru seçim:** hedef yalnızca resize değil de **yüksek DPI + çoklu monitör +
gelecekteki ekranlar** ise; ya da UI'a önümüzdeki aylarda ciddi yatırım yapılacaksa.

### 3.3 Seçenek (ii) — Hafif WinForms resize düzeltmesi

**Kapsam (kademeli, her adım kendi başına sevk edilebilir):**

- **Adım A — pencere esner (~0.5 gün).** `MaximumSize` kaldırılır, `MinimumSize`
  1000x680'de kalır (küçültme yasak → mutlak düzen bozulmaz). `FormBorderStyle.None`
  olduğu için **`WM_NCHITTEST` ile kenar tutamakları elle** eklenir (~40 satır) +
  başlık çubuğuna Büyüt/Geri Al düğmesi (`:373` yanına; Küçült zaten var).
  **`Region` her `Resize`'da yeniden üretilmeli** — `:270` bir kez hesaplıyor, esneyen
  pencerede içerik kırpılır. Dikkat: her boyutlandırmada yeni `HRGN` üretilirse eski
  `Region` bırakılmalı; tek seferlik çağrıda görünmeyen GDI sızıntısı burada gerçek olur.
  Sonuç: **daha çok alan görünür**, içerik sola yaslı kalır, `AutoScroll` zaten var
  (`:505,:562,:691,:796`) → küçük ekranda kaydırma, büyük ekranda boşluk.
- **Adım B — içerik doldurur (~1–1.5 gün).** En göze batan üçü esnetilir:
  `chartPcNet`/`chartNasNet` → `Dock`/`Anchor` (LineChart zaten ölçekleniyor, bedava
  kazanç); `pNet Height=240` → oransal; etiketlerdeki `MaximumSize=740` kaldırılır.
- **Adım C — donut'lar (~0.5 gün, opsiyonel).** `DonutProgress.OnPaint` sabit
  `Rectangle(20,15,100,100)` yerine `ClientSize`'dan hesaplasın. Küçük ama gerçek bir
  kalite sıçraması; A/B olmadan da yapılabilir.
- **Yapılmayacak:** Ayarlar sekmesinin `y += ...` akışını `TableLayoutPanel`'e çevirmek.
  Faydası düşük, regresyon riski yüksek; `AutoScroll` bunu zaten idare ediyor.

**Efor:** **A = 0.5 gün · A+B = ~2 gün · A+B+C = ~2.5 gün.**

**Risk: düşük–orta.** Gerçek riskler tek tek biliniyor ve küçük: `WM_NCHITTEST` ile
mevcut `ReleaseCapture`/`SendMessage(0xA1, 0x2)` sürükleme kodunun (`:310-311`) çakışması;
`Region` yeniden üretimi; `NoScrollPanel`'in `WM_HSCROLL`/`WM_VSCROLL` yutmasının
(`:37-39`) yeni kaydırma davranışıyla etkileşimi. Üçü de yerel ve geri alınabilir.

**UI tutarlılığı: kısa vadede iyi, uzun vadede tavan var.** Görünüm birebir korunur
(hiçbir font/efekt değişmez). Ama pencere çok büyütülürse **sağda ve altta boşluk** kalır
— 50 mutlak konumlu kontrol Anchor'lanmadıkça bu tam çözülmez. Yüksek DPI da düzelmez
(`AutoScaleMode` hiç ayarlanmamış).

### 3.4 Yan yana

| | (i) Avalonia | (ii) WinForms fix |
|---|---|---|
| Efor | 2–3 hafta | 0.5–2.5 gün |
| Risk | Yüksek (Faz 2 kazanımları yeniden doğrulanır) | Düşük–orta, yerel |
| Görsel kimlik | Yeniden üretilir, birebir değil | **Değişmez** |
| Gerçek esneklik | Tam | Kısmi (boşluk kalır) |
| Yüksek DPI | Çözülür | Çözülmez |
| F4-1 tray balonu | **Ek iş yaratır** (Avalonia'da balon yok) | Bedava (`NotifyIcon` var) |
| Geri dönüş | Zor | Kolay |
| Faz 4'ün gerisi | Donar | Etkilenmez |

### 3.5 Dwight'ın önerisi

**Şimdi (ii)-A+B, sonra ölç.** Gerekçe: kullanıcının şikâyeti "pencere kilitli", bu
~2 günde çözülüyor. Avalonia 2–3 haftayı ve Faz 2'nin yeniden doğrulanmasını istiyor,
karşılığında bugün *kimsenin şikâyet etmediği* bir şey (DPI, tam esneklik) veriyor.
(ii) yapıldıktan sonra (i) hâlâ mümkün — tersi doğru değil, çünkü (i) sırasında
(ii)'nin ürettiği değer aylarca yok.

**Ama (i) doğru seçim olur eğer** kullanıcı yüksek DPI/çoklu monitör sıkıntısı yaşıyorsa
ya da UI'a önümüzdeki 6 ayda ciddi yatırım planlanıyorsa. Bu bilgi bende yok — karar
kullanıcının.

---

## 4. D2 — Task 6: firmware `.hex` nereye?

**Çelişki:** `.gitignore:11` `*.hex` diyor, Faz 2 planı Task 6 `.hex`'i depoya istiyor
(`HANDOFF.md` "Blokajlar").

**Bugünkü durum:** `firmware/arduino_kontrol/arduino_kontrol.ino` **kaynak olarak depoda**
(+ `firmware/eski-versiyon.ino.txt`). Yani firmware zaten sürümleniyor; eksik olan yalnızca
derlenmiş çıktı.

**Öneri: `.hex` depoya girmesin; `!firmware/*.hex` istisnası açılmasın.**

1. `.hex` **türetilmiş çıktı**. Depoda `bin/`, `obj/`, `publish/`, `releases/` hepsi yok
   sayılıyor (`.gitignore`) — aynı ilke.
2. Kaynak (`.ino`) depoda olduğu için `.hex` her zaman yeniden üretilebilir; ikisini
   birden tutmak "ikisi uyuşmuyor" hatasını mümkün kılar, ki bu firmware'de teşhisi en
   pahalı hata sınıfıdır.
3. Kullanıcının gerçekten ihtiyacı olan şey **indirilebilir bir dosya**, git geçmişi değil.
   Depo artık public: `.hex`, GitHub Release'e **varlık olarak** eklenir (§5.2'ye bir
   satır). Kullanıcı tek tıkla indirir, geçmiş şişmez, dosya sürümle eşleşir.
4. `.gitignore`'a istisna açmak kuralı zayıflatır: bir sonraki kişi neden `publish/` için
   de açılmadığını sorar.

**Karşı argüman (dürüstlük payı):** Arduino IDE'si olmayan bir kullanıcı `.hex`'i kendisi
üretemez. Bu gerçek — ama release varlığı bunu zaten çözüyor.

**Kararı god'a bırakıyorum.** "Depoya girsin" denirse doğru biçim: `.gitignore`'a
`!firmware/*.hex` **artı** `firmware/README.md`'de hangi `.ino` sürümünden derlendiğinin
damgası (uyuşmazlık riskini bu azaltır, sıfırlamaz).

---

## 5. F4-3 — Nasıl release çıkılır (Task 7'nin kalanı)

`UpdateFeedUrl` doldu; **hiç release yayınlanmadığı için** güncelleme denetimi bugün
"güncelsiniz" demez, kaynağı boş bulup hata döner. Aşağıdaki akış bunu kapatır.

### 5.1 Paketleme (ölçüldü, tahmin değil)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/pack.ps1
```

`pack.ps1` sırayla: `vpk` var mı → **vpk sürümü csproj'daki Velopack ile aynı mı**
(`common.ps1`, review M2) → `publish.ps1` (yayın + sürüm damgası kapısı) → `vpk pack`.
Bu turda gerçekten çalıştırıldı; `releases/` çıktısı:

| Dosya | Boyut | Rol |
|---|---|---|
| `KontroXXL-win-Setup.exe` | 7.2 MB | **ilk kurulum** (kullanıcı bunu indirir) |
| `KontroXXL-2.2.0-full.nupkg` | 2.8 MB | **güncelleme paketi** — `UpdateManager` bunu indirir |
| `releases.win.json` | 251 B | **feed** — `GithubSource` önce bunu okur |
| `KontroXXL-win-Portable.zip` | 2.8 MB | kurulumsuz kopya (opsiyonel) |
| `RELEASES` | 78 B | eski biçim feed (geriye uyumluluk) |
| `assets.win.json` | 199 B | yerel manifest, `vpk upload` kullanır |

### 5.2 Yayınlama — `gh` ile (önerilen, denetlenebilir)

```powershell
# 1) Sürümü artır: Directory.Build.props içindeki <Version>
# 2) Paketle
powershell -NoProfile -ExecutionPolicy Bypass -File installer/pack.ps1
# 3) Yayınla (releases/ içindeki HER dosya)
gh release create v2.2.0 (Get-ChildItem releases\* | ForEach-Object { $_.FullName }) `
    --repo delibo74/KontroXXL `
    --title "KontroXXL 2.2.0" `
    --notes-file docs/release-notes-2.2.0.md
```

**Kurallar (Velopack sözleşmesi — üçü de sessiz başarısızlık üretir):**

- **`releases/` içindeki her dosyayı yükle**, seçme. `GithubSource` feed'i ve nupkg'yi
  ada göre arar; biri eksikse denetim başarısız olur.
- **Draft olmasın.** `accessToken` `null` geçiliyor (`TrayApplicationContext.cs:271`);
  kimliksiz istemci draft release'i **göremez**.
- **Pre-release olmasın.** `GithubSource(..., prerelease: false)`; pre-release işaretli
  bir yayın kullanıcılara hiç ulaşmaz.
- Etiket adı serbest (sürüm feed'den okunur); tutarlılık için `v<sürüm>`.
- `Directory.Build.props`'taki `<Version>` **önce** artırılır — sürümün tek kaynağı orası
  (`DOCS.md §12.2`), `pack.ps1` oradan okur ve damga kapısı uyuşmazlıkta durdurur.
- D2 "depoya girmesin" kabul edilirse: firmware `.hex` de bu release'e varlık olarak eklenir.

**Alternatif:** `vpk upload github --repoUrl ... --token ...` aynı işi yapar ve varlık
adlarını kendi seçer. `gh` tercih edildi: ne yüklendiği görülür ve `gh` zaten authed.

### 5.3 Yayından sonra doğrulama (spec §8'in borçlu maddeleri)

Bunlar **hâlâ ödenmedi** (`HANDOFF.md`: "bu makinede kurulum YAPILMADI"):

1. Temiz makinede `Setup.exe` → kurulum açılıyor mu (.NET 8 Desktop runtime bootstrap)
2. Tepside uygulama görünüyor, LCD bağlanıyor mu
3. `Directory.Build.props` 2.2.1 → `pack.ps1` → ikinci release → kurulu kopyada
   **"Güncellemeleri Denetle" gerçekten güncelliyor mu** — `UpdateFeedUrl`'ün ilk canlı sınavı
4. Kaldırma sonrası `%APPDATA%\KontroXXL` kalıyor mu (belgelenen davranış)
5. SmartScreen uyarısı: paket **imzasız** — beklenen, `README.md`'de yazılı

Bu beş adım yapılmadan Faz 4 "bitti" denemez.

---

## 6. Açık kararlar — god / kullanıcı

| # | Karar | Seçenekler | Dwight'ın önerisi |
|---|---|---|---|
| **D1** | Dashboard resize | (i) Avalonia geçişi · (ii) WinForms hafif fix | **(ii) A+B (~2 gün)**; Avalonia ancak yüksek DPI / çoklu monitör gerçek bir sorunsa |
| **D2** | Firmware `.hex` | depoya istisna · repo dışı + release varlığı | **repo dışı**, `.hex` GitHub Release'e varlık olarak |
| **D3** | İlk release ne zaman | şimdi 2.2.0 · F4-1 sonrası 2.3.0 | **şimdi 2.2.0** — güncelleme zincirinin canlı sınavı erken olsun; 2.3.0 o zincirin ilk gerçek müşterisi olur |

---

## 7. Yapılmayanlar (sınır)

Bu tur **plan + `UpdateFeedUrl`** ile sınırlıydı. Başlatılmadı: Avalonia geçişi ·
WinForms resize kodu · tray balonu implementasyonu · `.gitignore` değişikliği ·
GitHub release yayınlama · remote'a push (entegratör god).
