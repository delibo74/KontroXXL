# KontroXXL Project Summary & TODO

## 🚀 Current Architecture: "Thin Client v7.8"
The project has been migrated to a high-performance **Thin Client** architecture to overcome Arduino memory limitations.

- **Windows App (C# - .NET 8.0)**: Acts as the "Brain". It handles all data fetching (WMI, NVIDIA SMI, TrueNAS API), UI logic, menu navigation, and formatting.
- **Arduino (C++)**: Acts as the "Terminal". It only handles the physical LCD (16x2 I2C), rotary encoder input, and a back button. It communicates with the PC via a simple string-based protocol.

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
    - [x] Auto-reconnect serial system.
    - [x] Cache-based startup (no empty screens).
    - [x] Rotary encoder detent tolerance (debounce).

## 📝 TODO List
- [ ] **Multiple Pools**: Support for selecting and viewing multiple storage pools.
- [ ] **Config UI**: Add a setting in the Windows Dashboard to change update intervals.
- [ ] **Auto-Discovery**: Auto-detect Arduino COM port.
- [ ] **Theming**: Dark/Light mode support for the Windows Dashboard.

## 📈 Next Step: Graphical Stats
We are adding a new "Graphs" menu. This will use 8 custom characters mapped to different bar heights (0-8 pixels) to show a 16-column history graph of any tracked metric.
