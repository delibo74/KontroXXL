using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO.Ports;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Management;
using AudioSwitcher.AudioApi.CoreAudio;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.IO;
using KontroXXL.Core.Logging;

namespace KontroXXL_WinApp
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private MainForm mainForm;
        private AppConfig config;
        private SerialPort serialPort;
        private HttpClient httpClient;
        private CoreAudioController audioController;
        private System.Windows.Forms.Timer updateTimer;
        private PerformanceCounter cpuActualFreqCounter;
        
        private enum LcdMode { Home, Menu, Apps, Pools, Shortcuts, NasPower }
        private LcdMode currentLcdMode = LcdMode.Home;
        private int lcdIndex = 0;
        private int lcdPage = 0; // 0:CPU, 1:GPU, 2:NAS, 3:ALERTS
        private int scrollIdx = 0;
        private DateTime lastScrollUpdate = DateTime.Now;

        private JArray poolsList = new JArray();
        private JArray appsList = new JArray();
        private bool nasLastConn = false;
        private bool isSyncing = false;
        private string lastL0 = "", lastL1 = "";
        private DateTime volumeShowUntil = DateTime.MinValue;
        private DateTime lastLcdUpdate = DateTime.Now;
        private DateTime lastHeartbeat = DateTime.Now;
        private bool isUpdatingData = false;
        
        private long lastPcNetBytes = 0;
        private DateTime lastPcNetTime = DateTime.Now;

        private PerformanceCounter cpuUsageCounter;
        private PerformanceCounter ramUsageCounter;
        private (int, int, int) cachedGpuInfo = (0, 0, 0);
        private DateTime lastGpuQuery = DateTime.MinValue;
        private int cachedPcTemp = 0;
        private DateTime lastPcTempQuery = DateTime.MinValue;
        private string cachedNasInterface = null;

        private ILog log = NullLog.Instance;
        private JArray lastSvcsArr = new JArray();

        // LCD alert ticker
        private int _prevNasAlertCount = 0;
        private string _lcdTickerText = "";
        private DateTime _lcdTickerUntil = DateTime.MinValue;
        private int _tickerScrollIdx = 0;
        private DateTime _lastTickerScroll = DateTime.Now;

        public TrayApplicationContext()
        {
            try {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
                log = new RollingFileLogger(logFile, LogLevel.Info);
            } catch { log = NullLog.Instance; }
            Log("Uygulama baslatiliyor...");
            try
            {
                config = AppConfig.Load();
                
                if (config.EnableArduinoModule) InitSerial();

                var handler = new HttpClientHandler() {
                    // F19: SSL bypass scoped to TrueNAS IP only — not a blanket accept-all
                    ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => {
                        if (msg.RequestUri?.Host == config.TruenasIp) return true; // self-signed NAS cert
                        return errors == System.Net.Security.SslPolicyErrors.None;
                    }
                };
                httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                if (!string.IsNullOrEmpty(config.TruenasApiKey))
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.TruenasApiKey);

                audioController = new CoreAudioController();
                try { cpuActualFreqCounter = new PerformanceCounter("Processor Information", "Actual Frequency", "_Total"); } catch { }
                try { cpuUsageCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpuUsageCounter.NextValue(); } catch { }
                try { ramUsageCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use"); } catch { }

                mainForm = new MainForm(config);
                _ = mainForm.Handle;
                WireFormEvents(mainForm);

                // Build Menu FIRST
                var cms = new ContextMenuStrip();
                cms.Items.Add("Arayüzü Aç", null, (s, e) => ShowMainForm());
                cms.Items.Add("Yeniden Yükle", null, (s, e) => Reload());
                cms.Items.Add(new ToolStripSeparator());
                cms.Items.Add("Çıkış", null, (s, e) => { 
                    SendGoodbye();
                    trayIcon.Visible = false; 
                    Application.Exit(); 
                });

                Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Suspend) SendGoodbye(); };
                Microsoft.Win32.SystemEvents.SessionEnding += (s, e) => SendGoodbye();
                Microsoft.Win32.SystemEvents.SessionEnded += (s, e) => SendGoodbye();

                trayIcon = new NotifyIcon() { 
                    Icon = CreateIcon(), 
                    ContextMenuStrip = cms,
                    Text = "KontroXXL Tactical Dashboard",
                    Visible = true 
                };
                trayIcon.DoubleClick += (s, e) => ShowMainForm();

                // Main Update Loop (Unified)
                updateTimer = new System.Windows.Forms.Timer { Interval = 500 };
                updateTimer.Tick += (s, e) => {
                    // 1. Her zaman LCD'yi güncelle (Heartbeat dahil)
                    bool force = (DateTime.Now - lastHeartbeat).TotalSeconds > 4;
                    UpdateLCD(force);
                    if (force) lastHeartbeat = DateTime.Now;

                    // 2. Sistem bilgilerini arka planda, bloklamadan güncelle
                    if (!isUpdatingData)
                    {
                        isUpdatingData = true;
                        Task.Run(async () => {
                            try { await UpdateSystemInfo(); }
                            catch (Exception ex) { Log("Async update error: " + ex.Message); }
                            finally { isUpdatingData = false; }
                        });
                    }
                };
                updateTimer.Start();

                Log("Baslangic islemleri tamamlandi.");
            }
            catch (Exception ex)
            {
                Log("KRITIK HATA: " + ex.Message);
            }
        }

        private void Log(string msg) => log.Info(msg);

        private void WireFormEvents(MainForm form)
        {
            form.OnAppAction += (name, action) => Task.Run(() => AppAction(name, action));
            form.OnNasPower += (action) => Task.Run(async () => {
                try {
                    string endpoint = action == "REBOOT" ? "system/reboot" : "system/shutdown";
                    Log($"NAS {action} komutu arayuzden tetiklendi.");
                    await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/{endpoint}", null);
                } catch (Exception ex) { Log($"NAS {action} hatasi: {ex.Message}"); }
            });
            form.OnNasDismissAlerts += () => Task.Run(async () => {
                try {
                    Log("NAS Alertlar temizleniyor...");
                    string aJson = await httpClient.GetStringAsync($"https://{config.TruenasIp}/api/v2.0/alert/list");
                    if (string.IsNullOrEmpty(aJson)) return;
                    var alerts = JArray.Parse(aJson);
                    foreach (var al in alerts) {
                        if (!(bool)(al["dismissed"] ?? true)) {
                            string aid = al["id"]?.ToString();
                            if (!string.IsNullOrEmpty(aid))
                                await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/alert/dismiss", new StringContent(JsonConvert.SerializeObject(new { id = aid }), Encoding.UTF8, "application/json"));
                        }
                    }
                    Log("NAS Alertlar temizlendi.");
                } catch (Exception ex) { Log("Alert temizleme hatası: " + ex.Message); }
            });
            form.OnServiceAction += (idx, svc, act) => Task.Run(() => NasServiceAction(svc, act));
            form.OnShortcutsUpdate += () => { config.Save(); Log("Kısayollar güncellendi."); Task.Run(() => PushArduinoData()); };
            form.OnSettingsSaved += () => Reload();
        }

        private void ShowMainForm()
        {
            if (mainForm.IsDisposed) { mainForm = new MainForm(config); _ = mainForm.Handle; WireFormEvents(mainForm); }
            mainForm.Show();
            mainForm.BringToFront();
        }

        private void Reload()
        {
            Log("Ayarlar yeniden yukleniyor...");
            try {
                if (!string.IsNullOrEmpty(config.TruenasApiKey))
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.TruenasApiKey);
                else
                    httpClient.DefaultRequestHeaders.Authorization = null;

                if (serialPort != null && serialPort.IsOpen) { try { serialPort.Close(); } catch { } }
                if (config.EnableArduinoModule) InitSerial();

                nasLastConn = false;
                SyncNow();
            } catch (Exception ex) { Log("Yeniden yukleme hatasi: " + ex.Message); }
        }

        private Icon CreateIcon() { 
            try {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"); 
                if (File.Exists(p)) return new Icon(p);
                // System.Drawing.Icon.ExtractAssociatedIcon fails on some systems, use Fallback
                return SystemIcons.Shield; 
            } catch { return SystemIcons.Application; }
        }

        private void InitSerial() {
            try {
                string targetPort = config.ArduinoPort;
                bool needsDetect = string.IsNullOrEmpty(targetPort) || targetPort == "COM4";
                string detected = AutoDetectArduino();
                if (!string.IsNullOrEmpty(detected) && (needsDetect || !SerialPort.GetPortNames().Contains(targetPort))) {
                    Log($"Arduino otomatik algilandi: {detected}");
                    targetPort = detected;
                }

                if (string.IsNullOrEmpty(targetPort)) return;
                Log($"Seri port aciliyor: {targetPort} @ {config.ArduinoBaud} Baud");
                if (serialPort != null && serialPort.IsOpen) serialPort.Close();
                serialPort = new SerialPort(targetPort, config.ArduinoBaud);
                serialPort.DtrEnable = true;
                serialPort.RtsEnable = true;
                serialPort.DataReceived += (s, e) => {
                    try {
                        string line = serialPort.ReadLine()?.Trim();
                        if (string.IsNullOrEmpty(line)) return;
                        log.Debug("Arduino'dan gelen: " + line);

                        if (line.StartsWith("EV:")) HandleArduinoEvent(line.Substring(3));
                        else if (line == "CMD:READY" || line == "CMD:UPDATE") { UpdateLCD(true); Task.Run(() => PushArduinoData()); }
                        else if (line == "CMD:APPS" || line == "CMD:POOLS" || line == "CMD:SHORTCUTS") { Task.Run(() => PushArduinoData()); }
                    } catch (Exception ex) { Log("Seri veri isleme hatasi: " + ex.Message); }
                };
                serialPort.Open();
                Log("Seri port acildi.");
                SendData("ON");
                config.ArduinoPort = targetPort; config.Save();
            } catch (Exception ex) { Log("Seri port acma hatasi: " + ex.Message); }
        }

        private string AutoDetectArduino() {
            try {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'")) {
                    var ports = searcher.Get();
                    foreach (var port in ports) {
                        string caption = port["Caption"]?.ToString();
                        if (string.IsNullOrEmpty(caption)) continue;
                        if (caption.Contains("Arduino") || caption.Contains("USB Serial") || caption.Contains("CH340") || caption.Contains("CP210")) {
                            int start = caption.LastIndexOf("(COM") + 1;
                            int end = caption.LastIndexOf(")");
                            if (start > 0 && end > start) return caption.Substring(start, end - start);
                        }
                    }
                }
            } catch { }
            var pNames = SerialPort.GetPortNames();
            return pNames.Length > 0 ? pNames[pNames.Length - 1] : null;
        }

        private async Task UpdateSystemInfo() {
            try {
                int wc = (int)GetCpuUsage(), wr = (int)GetRamUsage();
                double wn = GetPcNetSpeed(), cf = GetCpuSpeed();
                var gpu = GetGpuInfo(); // (util, temp, fan)
                int pt = GetPcTemp();

                config.LastCpu = wc; config.LastRam = wr; config.LastNetSpeed = wn; config.LastCpuFreq = cf;
                config.LastGpu = gpu.Item1; config.LastGpuTemp = gpu.Item2; config.LastGpuFan = gpu.Item3;
                config.LastPcTemp = pt;

                if (config.EnableNasModule && !string.IsNullOrEmpty(config.TruenasIp)) {
                    var nas = await GetTruenasData();
                    if (nas.conn && !nasLastConn) {
                        Log("NAS baglantisi saglandı, Arduino guncelleniyor.");
                        await PushArduinoData();
                    }
                    nasLastConn = nas.conn;
                     if (nas.conn) {
                          config.LastNasCpu = nas.nc; config.LastNasTemp = nas.nt;
                          config.LastNasRx = nas.nrx; config.LastNasTx = nas.ntx; 
                          config.LastNasLoad = nas.nl; config.LastNasAlerts = nas.na;
                          config.LastNasUptime = nas.up; config.LastNasMem = nas.mem;
                          config.LastNasServicesJ = nas.svcs; config.LastNasAlertsJ = nas.alerts;
                          config.LastPools = nas.pools; config.LastNasAppsJ = appsList;
                     }
                }
                // config.Save(); // Telemetry verilerini her saniye diske yazmak performansı etkileyebilir, kaldırıldı.

                if (!mainForm.IsDisposed) mainForm.UpdateStats(wc, wr, gpu.Item1, gpu.Item2, gpu.Item3, cf, wn);
            } catch (Exception ex) { Log("UpdateSystemInfo error: " + ex.Message); }
        }

        private void HandleArduinoEvent(string ev)
        {
            switch (ev)
            {
                case "UP":
                    if (currentLcdMode == LcdMode.Home) { 
                        try {
                            var dev = audioController.DefaultPlaybackDevice;
                            if (dev != null) { dev.Volume += 2; volumeShowUntil = DateTime.Now.AddSeconds(2); }
                        } catch { }
                    }
                    else { lcdIndex++; FixIndex(); }
                    break;
                case "DN":
                    if (currentLcdMode == LcdMode.Home) { 
                        try {
                            var dev = audioController.DefaultPlaybackDevice;
                            if (dev != null) { dev.Volume -= 2; volumeShowUntil = DateTime.Now.AddSeconds(2); }
                        } catch { }
                    }
                    else { lcdIndex--; FixIndex(); }
                    break;
                case "CLICK":
                    if (currentLcdMode == LcdMode.Home) { currentLcdMode = LcdMode.Menu; lcdIndex = 0; }
                    else if (currentLcdMode == LcdMode.Menu) {
                        if (lcdIndex == 0)      { currentLcdMode = LcdMode.Apps; lcdIndex = 0; _ = PushArduinoData(); }
                        else if (lcdIndex == 1) { currentLcdMode = LcdMode.Pools; lcdIndex = 0; _ = PushArduinoData(); }
                        else if (lcdIndex == 2) { currentLcdMode = LcdMode.Shortcuts; lcdIndex = 0; }
                        else if (lcdIndex == 3) { currentLcdMode = LcdMode.NasPower; lcdIndex = 0; }
                    }
                    else if (currentLcdMode == LcdMode.Apps) { ToggleApp(lcdIndex); }
                    else if (currentLcdMode == LcdMode.Shortcuts) { RunShortcut(lcdIndex); }
                    else if (currentLcdMode == LcdMode.NasPower) { HandleNasPower(lcdIndex); }
                    break;
                case "BACK":
                    if (currentLcdMode == LcdMode.Home) {
                        lcdPage = (lcdPage + 1) % 4; // 0:CPU, 1:GPU, 2:NAS, 3:ALERTS
                        SendData("CLR"); // Clear stale chars between page transitions
                        lastL0 = ""; lastL1 = ""; // Force full redraw after CLR
                    } else {
                        currentLcdMode = LcdMode.Home; lcdIndex = 0;
                        SendData("CLR");
                        lastL0 = ""; lastL1 = "";
                    }
                    break;
            }
            UpdateLCD(true);
        }

        private void FixIndex()
        {
            int max = 0;
            switch (currentLcdMode) {
                case LcdMode.Menu: max = 4; break;
                case LcdMode.Apps: max = appsList.Count; break;
                case LcdMode.Pools: max = poolsList.Count; break;
                case LcdMode.Shortcuts: max = config.Shortcuts.Count; break;
                case LcdMode.NasPower: max = 3; break;
            }
            if (max == 0) { lcdIndex = 0; return; }
            if (lcdIndex < 0) lcdIndex = max - 1;
            if (lcdIndex >= max) lcdIndex = 0;
            scrollIdx = 0;
        }

        private void UpdateLCD(bool forced = false)
        {
            if (serialPort == null || !serialPort.IsOpen) return;
            
            // Throttle rapid updates to prevent LCD bugging (min 40ms between forced sends)
            var now = DateTime.Now;
            if (forced && (now - lastLcdUpdate).TotalMilliseconds < 30) return;
            if (!forced && (now - lastLcdUpdate).TotalMilliseconds < 100) return;
            lastLcdUpdate = now;

            try
            {
                string l0 = "", l1 = "";
                bool isBar = false; int barVal = 0;

                switch (currentLcdMode)
                {
                    case LcdMode.Home:
                        if (DateTime.Now < volumeShowUntil) {
                            try {
                                var dev = audioController.DefaultPlaybackDevice;
                                if (dev != null) {
                                    l0 = " SYSTEM VOLUME  ";
                                    isBar = true; barVal = (int)dev.Volume;
                                }
                            } catch { l0 = " VOLUME ERROR   "; l1 = " Check Audio!   "; }
                        } else if (lcdPage == 0) {
                            string cpuLeft   = string.Format("CPU:{0}%", config.LastCpu);
                            string freqRight = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00}G", config.LastCpuFreq);
                            l0 = (cpuLeft + freqRight.PadLeft(16 - cpuLeft.Length)).PadRight(16).Substring(0, 16);

                            string ramLeft   = string.Format("RAM:{0}%", config.LastRam);
                            string clockRight = DateTime.Now.ToString("HH:mm");
                            l1 = (ramLeft + clockRight.PadLeft(16 - ramLeft.Length)).PadRight(16).Substring(0, 16);
                        } else if (lcdPage == 1) {
                            l0 = string.Format("GPU:{0}% {1}C", config.LastGpu, config.LastGpuTemp).PadRight(16).Substring(0, 16);
                            l1 = string.Format("Fan:{0}% {1}Mbps", config.LastGpuFan, (int)config.LastNetSpeed).PadRight(16).Substring(0, 16);
                        } else if (lcdPage == 2) {
                            if (!nasLastConn) { l0 = "  NAS: OFFLINE  "; l1 = " No Connection  "; }
                            else {
                                l0 = string.Format("NAS:{0}% {1}C", config.LastNasCpu, config.LastNasTemp).PadRight(16).Substring(0, 16);
                                l1 = string.Format("\x01{0,3}Mb \x02{1,3}Mb   ", (int)config.LastNasRx, (int)config.LastNasTx);
                            }
                        } else {
                            l0 = "> NAS DASHBOARD ";
                            l1 = config.LastNasAlerts == 0 ? "No active alerts" : $"{config.LastNasAlerts} SYSTEM ALERTS!";
                        }

                        // Alert ticker override: show scrolling notification on l0 (not when on alert page)
                        if (DateTime.Now < _lcdTickerUntil && lcdPage != 3 && !string.IsNullOrEmpty(_lcdTickerText)) {
                            l0 = GetTicker();
                        }
                        break;
                    case LcdMode.Menu:
                        l0 = "> SYSTEM MENU   ";
                        if (lcdIndex == 0)      l1 = "1. NAS APPS     ";
                        else if (lcdIndex == 1) l1 = "2. NAS POOLS    ";
                        else if (lcdIndex == 2) l1 = "3. SHORTCUTS    ";
                        else                    l1 = "4. NAS POWER    ";
                        break;
                    case LcdMode.Apps:
                        if (appsList.Count == 0) { l0 = " APPLICATIONS   "; l1 = " Syncing...      "; }
                        else {
                            l0 = GetScrolled(appsList[lcdIndex]["name"]?.ToString() ?? "");
                            string st = appsList[lcdIndex]["state"]?.ToString();
                            l1 = (st == "RUNNING" || st == "ACTIVE") ? ">> RUNNING <<   " : ">> STOPPED <<   ";
                        }
                        break;
                    case LcdMode.Pools:
                        if (poolsList.Count == 0) { l0 = " STORAGE POOLS  "; l1 = " Syncing...      "; }
                        else {
                            l0 = GetScrolled(poolsList[lcdIndex]["name"]?.ToString() ?? "");
                            isBar = true; barVal = (int)poolsList[lcdIndex]["used"];
                        }
                        break;
                    case LcdMode.Shortcuts:
                        if (config.Shortcuts.Count == 0) { l0 = "ACTIONS:        "; l1 = " No Shortcuts   "; }
                        else { l0 = "ACTIONS:        "; l1 = GetScrolled(config.Shortcuts[lcdIndex].Name); }
                        break;
                    case LcdMode.NasPower:
                        l0 = "> NAS POWER     ";
                        if (lcdIndex == 0)      l1 = "1. NAS REBOOT   ";
                        else if (lcdIndex == 1) l1 = "2. NAS SHUTDOWN ";
                        else                    l1 = "3. CANCEL       ";
                        break;
                }

                if (!string.IsNullOrEmpty(l0) && (forced || l0 != lastL0)) { SendData("L0=" + l0); lastL0 = l0; }
                if (isBar || (!string.IsNullOrEmpty(l1) && (forced || l1 != lastL1))) {
                    if (isBar) { SendData("B1=" + barVal); lastL1 = "BAR_" + barVal; }
                    else { SendData("L1=" + l1); lastL1 = l1; }
                }
            } catch { }
        }

        private string GetScrolled(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= 16) return text.PadRight(16);
            if (DateTime.Now.Subtract(lastScrollUpdate).TotalMilliseconds > 400) {
                scrollIdx++;
                if (scrollIdx > text.Length + 2) scrollIdx = 0;
                lastScrollUpdate = DateTime.Now;
            }
            string extended = text + "  " + text;
            try { return extended.Substring(scrollIdx, 16); } catch { scrollIdx = 0; return text.PadRight(16); }
        }

        private string GetTicker()
        {
            string text = _lcdTickerText;
            if (string.IsNullOrEmpty(text)) return "";
            if (DateTime.Now.Subtract(_lastTickerScroll).TotalMilliseconds > 300) {
                _tickerScrollIdx++;
                if (_tickerScrollIdx > text.Length + 2) _tickerScrollIdx = 0;
                _lastTickerScroll = DateTime.Now;
            }
            string extended = text + "  " + text;
            try { return extended.Substring(_tickerScrollIdx, 16); } catch { _tickerScrollIdx = 0; return text.PadRight(16).Substring(0, 16); }
        }

        private void SendData(string msg)
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                try { serialPort.Write(msg + "\n"); } catch { }
            }
        }

        private void SendGoodbye()
        {
            SendData("CLR");
            SendData("L0=    BYE BYE!    ");
            SendData("L1=  SYSTEM OFF?   ");
            SendData("OFF"); // Fast trigger
            System.Threading.Thread.Sleep(500); 
        }

        private void ToggleApp(int idx)
        {
            if (idx < 0 || idx >= appsList.Count) return;
            string name = appsList[idx]["name"]?.ToString() ?? "";
            string state = appsList[idx]["state"]?.ToString() ?? "";
            string action = (state == "RUNNING" || state == "ACTIVE") ? "STOP" : "START";
            _ = AppAction(name, action);
        }

        private void RunShortcut(int idx)
        {
            if (idx >= 0 && idx < config.Shortcuts.Count)
            {
                try { Process.Start(new ProcessStartInfo(config.Shortcuts[idx].Path) { UseShellExecute = true, Arguments = config.Shortcuts[idx].Arguments }); }
                catch { }
                currentLcdMode = LcdMode.Home;
            }
        }

        private void HandleNasPower(int idx)
        {
            if (idx == 0 || idx == 1)
            {
                string action = (idx == 0) ? "REBOOT" : "SHUTDOWN";
                Task.Run(async () => {
                    try {
                        string endpoint = (action == "REBOOT") ? "system/reboot" : "system/shutdown";
                        Log($"NAS {action} komutu Arduino'dan tetiklendi.");
                        await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/{endpoint}", null);
                    } catch (Exception ex) { Log($"NAS {action} hatasi: {ex.Message}"); }
                });
            }
            currentLcdMode = LcdMode.Home;
        }

        private async Task<(int nc, double nrx, double ntx, double nl, int nt, int na, string up, string mem, JArray svcs, bool conn, JArray pools, JArray alerts)> GetTruenasData() {
            try {
                string baseUrl = $"https://{config.TruenasIp}/api/v2.0";

                // Phase 1: Fire ALL independent requests in parallel (F3 optimization)
                var sysTask = httpClient.GetStringAsync($"{baseUrl}/system/info");
                var cpuTask = TruenasPost("reporting/get_data", new { graphs = new[] { new { name = "cpu" } } });
                var tempTask = TruenasPost("reporting/get_data", new { graphs = new[] { new { name = "cputemp" } } });
                var memTask = TruenasPost("reporting/get_data", new { graphs = new[] { new { name = "memory" } } });
                string netIface = cachedNasInterface ?? "enp3s0";
                var netTask = TruenasPost("reporting/get_data", new { graphs = new[] { new { name = "interface", identifier = netIface } } });
                var poolTask = SafeGetString($"{baseUrl}/pool");
                var alertTask = SafeGetString($"{baseUrl}/alert/list");
                var svcTask = SafeGetString($"{baseUrl}/service");

                await Task.WhenAll(sysTask, cpuTask, tempTask, memTask, netTask, poolTask, alertTask, svcTask);

                // Phase 2: Parse results
                var sys = JObject.Parse(await sysTask);
                int nc = 0, nt = 0, na = 0; double nrx = 0, ntx = 0, nl = (double)(sys["loadavg"]?[0] ?? 0.0);
                string uptime = "", memory = "";

                if (sys["uptime_seconds"] != null) {
                    TimeSpan t = TimeSpan.FromSeconds((long)sys["uptime_seconds"]);
                    uptime = t.Days > 0 ? $"{t.Days}d {t.Hours}h {t.Minutes}m" : $"{t.Hours}h {t.Minutes}m {t.Seconds}s";
                }

                // Parse CPU
                try { var cpuRes = await cpuTask; if (cpuRes != null && cpuRes[0]["data"]?.Last != null) nc = (int)Math.Round((double)cpuRes[0]["data"].Last[1]); } catch { }

                // Parse Temp
                try { var tempRes = await tempTask; if (tempRes != null && tempRes[0]["data"]?.Last != null) { var v = tempRes[0]["data"].Last.Children().Skip(1).Where(x => x.Type != JTokenType.Null).Select(x => (double)x); if (v.Any()) nt = (int)v.Max(); } } catch { }

                // Parse Memory
                if (sys["physmem"] != null) {
                    double totalGb = (double)sys["physmem"] / (1024.0 * 1024 * 1024);
                    double freeGb = 0;
                    try { var memRes = await memTask; if (memRes != null && memRes[0]["data"]?.Last != null) freeGb = (double)(memRes[0]["data"].Last[1] ?? 0.0) / (1024.0 * 1024 * 1024); } catch { }
                    memory = $"{(totalGb - freeGb):0.0}/{totalGb:0.0}GB";
                }

                // Parse Network (with auto-detect and caching)
                try {
                    var netRes = await netTask;
                    if (netRes != null && netRes is JArray && netRes[0]["data"] != null) {
                        var d = netRes[0]["data"] as JArray;
                        for (int k = d.Count - 1; k >= 0; k--) {
                            var pj = d[k] as JArray;
                            if (pj != null && pj.Count >= 3 && pj[1].Type != JTokenType.Null) { nrx = (double)pj[1] / 1000.0; ntx = (double)pj[2] / 1000.0; break; }
                        }
                    }
                    if (nrx == 0 && ntx == 0 && cachedNasInterface == null) {
                        foreach (var path in new[] { "en7x", "eno1", "eth0" }) {
                            var fallback = await TruenasPost("reporting/get_data", new { graphs = new[] { new { name = "interface", identifier = path } } });
                            if (fallback != null && fallback is JArray && fallback[0]["data"] != null) {
                                var d2 = fallback[0]["data"] as JArray;
                                for (int k = d2.Count - 1; k >= 0; k--) { var pj = d2[k] as JArray; if (pj != null && pj.Count >= 3 && pj[1].Type != JTokenType.Null) { nrx = (double)pj[1] / 1000.0; ntx = (double)pj[2] / 1000.0; cachedNasInterface = path; break; } }
                            }
                            if (nrx > 0 || ntx > 0) break;
                        }
                    } else if (nrx > 0 || ntx > 0) { cachedNasInterface = netIface; }
                } catch { }

                // Parse Pools
                JArray poolArr = new JArray(), alertsArr = new JArray(), svcsArr = new JArray();
                try { string poolStr = await poolTask; if (!string.IsNullOrEmpty(poolStr)) { poolArr = JArray.Parse(poolStr); foreach (var p in poolArr) { long sz = (long)(p["size"] ?? 0L), usd = (long)(p["allocated"] ?? 0L); p["used"] = sz > 0 ? (int)(usd * 100 / sz) : 0; } } } catch { }
                try { string alertStr = await alertTask; if (!string.IsNullOrEmpty(alertStr)) { var aRaw = JArray.Parse(alertStr); alertsArr = new JArray(aRaw.Where(a => !(bool)(a["dismissed"] ?? true))); na = alertsArr.Count; } } catch { }
                try { string svcStr = await svcTask; if (!string.IsNullOrEmpty(svcStr)) svcsArr = JArray.Parse(svcStr); } catch { }

                lastSvcsArr = svcsArr;
                if (!mainForm.IsDisposed) mainForm.UpdateNasStats(nc, nrx, ntx, nl, nt, poolArr, appsList, alertsArr, uptime, memory, svcsArr);

                // Alert ticker: trigger LCD scrolling notification when new alerts arrive
                if (na > _prevNasAlertCount && na > 0) {
                    _lcdTickerText = $"! YENI ALARM: {na} uyari aktif !  ";
                    _lcdTickerUntil = DateTime.Now.AddSeconds(10);
                    _tickerScrollIdx = 0;
                    Log($"LCD ticker tetiklendi: {na} yeni uyari.");
                }
                _prevNasAlertCount = na;

                return (nc, nrx, ntx, nl, nt, na, uptime, memory, svcsArr, true, poolArr, alertsArr);
            } catch (Exception ex) { Log("GetTruenasData error: " + ex.Message); return (0,0,0,0,0,0, "","", new JArray(), false, new JArray(), new JArray()); }
        }

        private async Task NasServiceAction(string service, string action) {
            try {
                // F13: use cached service list from last poll instead of extra API call
                var svc = lastSvcsArr.FirstOrDefault(s => (string)s["service"] == service);
                if (svc == null) {
                    // Fallback: fetch if cache is empty
                    var sRaw = await SafeGetString($"https://{config.TruenasIp}/api/v2.0/service");
                    if (string.IsNullOrEmpty(sRaw)) return;
                    svc = JArray.Parse(sRaw).FirstOrDefault(s => (string)s["service"] == service);
                    if (svc == null) return;
                }
                int id = (int)svc["id"];
                await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/service/{(action == "START" ? "start" : "stop")}", new StringContent(JsonConvert.SerializeObject(new { id = id }), Encoding.UTF8, "application/json"));
                Log($"NAS Service {service} {(action == "START" ? "started" : "stopped")}.");
            } catch (Exception ex) { Log("ServiceAction Error: " + ex.Message); }
        }

        private async Task PushArduinoData() {
            if (isSyncing) return;
            isSyncing = true;
            try {
                Log("Arduino tam senkronizasyon baslatildi...");
                
                var apps = JArray.Parse(await httpClient.GetStringAsync($"https://{config.TruenasIp}/api/v2.0/app"));
                appsList = new JArray(apps.OrderBy(x => x["name"]?.ToString()));

                var pools = JArray.Parse(await httpClient.GetStringAsync($"https://{config.TruenasIp}/api/v2.0/pool"));
                poolsList = new JArray();
                foreach (var p in pools) {
                    long sz = (long)(p["size"] ?? 0L), usd = (long)(p["allocated"] ?? 0L);
                    poolsList.Add(new JObject { ["name"] = p["name"], ["used"] = sz > 0 ? (int)(usd * 100 / sz) : 0 });
                }

                UpdateLCD(true);
            } catch { }
            finally { 
                isSyncing = false; 
                Log("Arduino senkronizasyonu tamamlandi.");
            }
        }



        private async Task AppAction(string name, string action) {
            try {
                await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/app/{action.ToLower()}", new StringContent(JsonConvert.SerializeObject(name), Encoding.UTF8, "application/json"));
                await Task.Delay(1000);
                await PushArduinoData();
            } catch (Exception ex) { Log("AppAction error: " + ex.Message); }
        }

        private async Task<JToken> TruenasPost(string ep, object data) {
            try {
                var res = await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/{ep}", new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json"));
                return res.IsSuccessStatusCode ? JToken.Parse(await res.Content.ReadAsStringAsync()) : null;
            } catch { return null; }
        }

        private async Task<string> SafeGetString(string url) {
            try { return await httpClient.GetStringAsync(url); } catch { return null; }
        }

        private double GetPcNetSpeed() {
            try {
                var now = DateTime.Now;
                long b = 0;
                // F21: foreach instead of LINQ Where+Sum — no iterator allocation
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (iface.OperationalStatus != OperationalStatus.Up) continue;
                    if (iface.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                        iface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    var stats = iface.GetIPStatistics();
                    b += stats.BytesReceived + stats.BytesSent;
                }
                
                if (lastPcNetBytes == 0) { lastPcNetBytes = b; lastPcNetTime = now; return 0; }
                
                double dt = (now - lastPcNetTime).TotalSeconds;
                long d = b - lastPcNetBytes;
                lastPcNetBytes = b; lastPcNetTime = now;
                return (dt > 0.1 && d >= 0) ? Math.Round((d * 8) / (1e6 * dt), 2) : 0;
            } catch { return 0; }
        }

        private float GetCpuUsage() { try { return cpuUsageCounter?.NextValue() ?? 0; } catch { return 0; } }
        private double GetCpuSpeed() { try { return cpuActualFreqCounter != null ? Math.Round(cpuActualFreqCounter.NextValue() / 1000.0, 2) : 0; } catch { return 0; } }
        private int GetRamUsage() { try { return (int)(ramUsageCounter?.NextValue() ?? 0); } catch { return 0; } }
        private int GetPcTemp() {
            if ((DateTime.Now - lastPcTempQuery).TotalSeconds < 5) return cachedPcTemp;
            try { using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature")) foreach (ManagementObject o in s.Get()) { cachedPcTemp = (int)((Convert.ToDouble(o["CurrentTemperature"]) - 2732) / 10.0); lastPcTempQuery = DateTime.Now; return cachedPcTemp; } } catch { }
            return cachedPcTemp;
        }
        private (int, int, int) GetGpuInfo() {
            if ((DateTime.Now - lastGpuQuery).TotalSeconds < 2) return cachedGpuInfo;
            try {
                var si = new ProcessStartInfo { FileName = "nvidia-smi", Arguments = "--query-gpu=utilization.gpu,temperature.gpu,fan.speed --format=csv,noheader,nounits", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(si)) {
                    if (!p.WaitForExit(2000)) { p.Kill(); return cachedGpuInfo; }
                    var o = p.StandardOutput.ReadToEnd().Split(',');
                    if (o.Length >= 3) cachedGpuInfo = (int.Parse(o[0].Trim()), int.Parse(o[1].Trim()), int.Parse(o[2].Trim()));
                    else if (o.Length >= 2) cachedGpuInfo = (int.Parse(o[0].Trim()), int.Parse(o[1].Trim()), 0);
                    lastGpuQuery = DateTime.Now;
                }
            } catch { }
            return cachedGpuInfo;
        }


        private void SyncNow() { 
            Log("Startup Sync initiated."); 
            if (!mainForm.IsDisposed) 
                mainForm.UpdateNasStats(config.LastNasCpu, config.LastNasRx, config.LastNasTx, config.LastNasLoad, config.LastNasTemp, config.LastPools, config.LastNasAppsJ, config.LastNasAlertsJ, config.LastNasUptime, config.LastNasMem, config.LastNasServicesJ);

            if (config.EnableNasModule) Task.Run(async () => { await Task.Delay(1000); await PushArduinoData(); }); 
        }
    }
}
