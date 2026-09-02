# KontroXXL Project Summary & TODO

## 🚀 Current Architecture: "Thin Client" (v2.1.0)
The project has been migrated to a high-performance **Thin Client** architecture to overcome Arduino memory limitations.

- **Windows App (C# - .NET 8.0)**: Acts as the "Brain". It handles all data fetching (WMI, NVIDIA SMI, TrueNAS API), UI logic, menu navigation, and formatting.
- **Arduino (C++)**: Acts as the "Terminal". It only handles the physical LCD (16x2 I2C), rotary encoder input, and a back button. It communicates with the PC via a simple string-based protocol.

**Faz 1 (sağlamlaştırma) güncellemesi:** Windows App artık tek proje değil, iki proje. `src/KontroXXL.Core` platformdan bağımsız saf mantığı (LCD biçimlendirme, menü durum makinesi, log, config) barındırır ve 150 xUnit testiyle donanımsız doğrulanır; `src/KontroXXL_WinApp` WinForms UI'sini, tray'i ve donanım erişimini taşır. Bkz. `DOCS.md` §5.3.

## 📡 Communication Protocol
| Direction | Command | Description |
|-----------|---------|-------------|
| PC -> ARD | `L0=text` | Set top line (16 chars) |
| PC -> ARD | `L1=text` | Set bottom line (16 chars) |
| PC -> ARD | `B1=val`  | Draw horizontal progress bar on line 1 (0-100) |
| ARD -> PC | `EV:UP`   | Rotary encoder turned right |
| ARD -> PC | `EV:DN`   | Rotary encoder turned left |
| ARD -> PC | `EV:CLICK`| Rotary encoder button clicked |
| ARD -> PC | `EV:BACK` | Physical back button clicked |

## ✅ Completed Features
- [x] **Thin Client Migration**: All logic offloaded to PC.
- [x] **PC Telemetry**: Real-time CPU usage, GHz, RAM, GPU Use, GPU Temp, GPU Fan, Network Speed (Mbps).
- [x] **TrueNAS Integration**: 
    - [x] CPU/Temp/Load monitoring.
    - [x] Storage Pool usage display.
    - [x] Application management (Start/Stop).
    - [x] Remote Power Control (Reboot/Shutdown).
- [x] **Navigation**: Rotary encoder menu system with page cycling.
- [x] **Reliability**: 
    - [x] Mutex-based single instance control.
    - [x] Auto-reconnect serial system — `SerialLink`, 2 saniyelik izleyici döngüsüyle kablo çekilip takıldığında kendini toparlar (Faz 1, A2).
    - [x] Cache-based startup — artık gerçekten diske iniyor: atomik yazım + dirty-flag flush (Faz 1, A4). Bu yalnızca **LCD** için geçerli (`BuildViewData` önbelleği açılışta okur); **WinForms dashboard'u** önbelleği açılışta göstermiyor — donutlar ilk telemetri tick'ine kadar boş/0 kalıyor, çünkü önbelleği UI'ya basan `SyncNow()`'ı açılışta çağıran bir yol yok (bkz. `DOCS.md` §7).
    - [x] Rotary encoder detent tolerance (debounce).
    - [x] Log rotation — `app.log` 1 MB'ı geçince `app.1.log`/`.2`/`.3`'e döner, sınırsız büyümüyor (Faz 1, A3).
    - [x] Auto-Discovery — `SerialLink.DetectArduinoPort` WMI ile Arduino/CH340/CP210x cihazını arıyor; Ayarlar'da COM port alanı boş bırakılırsa devreye giriyor.

## 📝 TODO List
- [ ] **Multiple Pools**: Support for selecting and viewing multiple storage pools.
- [ ] **Config UI**: Add a setting in the Windows Dashboard to change update intervals (bugün yalnızca `config.json`'dan elle değiştiriliyor — `LcdIntervalMs`/`PcIntervalMs`/`NasIntervalMs`/`ConfigFlushIntervalMs`. **Önce uygulamayı kapat** (tray → Çıkış): açıkken düzenlersen `flushTimer` değişikliği en geç `ConfigFlushIntervalMs` içinde üzerine yazar).
- [ ] **Theming**: Dark/Light mode support for the Windows Dashboard.
- [ ] **Resizable Dashboard (Faz 4 — Avalonia)**: `MainForm` penceresi hâlâ sabit boyutlu (`MinimumSize == MaximumSize`).

## 📈 Next Step: Graphical Stats
We are adding a new "Graphs" menu. This will use 8 custom characters mapped to different bar heights (0-8 pixels) to show a 16-column history graph of any tracked metric.
