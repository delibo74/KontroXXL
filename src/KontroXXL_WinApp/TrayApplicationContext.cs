using System;
using System.Drawing;
using System.Windows.Forms;
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
using KontroXXL.Core.Lcd;
using KontroXXL.Core.Configuration;

namespace KontroXXL_WinApp
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private MainForm mainForm;
        private AppConfig config;
        private AppPaths paths;
        private KontroXXL.Core.Security.ISecretProtector secrets;
        private SerialLink serial;
        private HttpClient httpClient;
        private CoreAudioController audioController;
        private System.Windows.Forms.Timer lcdTimer, pcTimer, nasTimer, flushTimer;
        private bool isPcUpdating = false;
        private bool isNasUpdating = false;
        private PerformanceCounter cpuActualFreqCounter;
        
        // LCD durumu tek bir referansta toplandı; yalnızca UI thread'inden değiştirilir (A7).
        private LcdMenuState lcdState = LcdMenuState.Initial;
        private int scrollOffset = 0;
        private DateTime lastScrollTick = DateTime.Now;

        private JArray poolsList = new JArray();
        private JArray appsList = new JArray();
        private bool nasLastConn = false;
        private bool isSyncing = false;
        private string lastL0 = "", lastL1 = "";
        private DateTime volumeShowUntil = DateTime.MinValue;
        private DateTime lastLcdUpdate = DateTime.Now;
        private DateTime lastHeartbeat = DateTime.Now;

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
            // A6: yazilabilir durum artik %APPDATA%\KontroXXL altinda. Logger da bu yolu
            // kullanacagi icin goc, logger kurulumundan once ve ctor'un en basinda calisir.
            paths = AppPaths.ForCurrentUser();
            Directory.CreateDirectory(paths.Root);

            string legacyConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            bool migrated = ConfigMigrator.MigrateIfNeeded(legacyConfig, paths.ConfigFile);

            try {
                log = new RollingFileLogger(paths.LogFile, LogLevel.Info);
            } catch { log = NullLog.Instance; }
            Log("Uygulama baslatiliyor...");
            try
            {
                config = AppConfig.Load(paths.ConfigFile);
                if (migrated)
                {
                    lock (config.SyncRoot)
                    {
                        config.SchemaVersion = 3;
                        config.MarkDirty();
                    }
                }

                secrets = new DpapiSecretProtector();
                if (config.UnprotectSecrets(secrets)) { config.MarkDirty(); log.Info("API anahtari sifrelendi (goc)."); }
                if (config.SecretUnreadable) log.Info("API anahtari cozulemedi — Ayarlar'dan yeniden girilmeli.");

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
                mainForm.Secrets = secrets;
                _ = mainForm.Handle;

                // C-3 (M-3): InitSerial() seri okuma dongusunu hemen baslatir; Connected
                // olayi RunOnUi/HandleArduinoEvent uzerinden mainForm'a UI thread'ine
                // marshal edilir. Bu nedenle mainForm ve handle'i olusmadan cagrilmamali.
                if (config.EnableArduinoModule) InitSerial();

                WireFormEvents(mainForm);

                // Build Menu FIRST
                var cms = new ContextMenuStrip();
                cms.Items.Add("Arayüzü Aç", null, (s, e) => ShowMainForm());
                cms.Items.Add("Yeniden Yükle", null, (s, e) => Reload());
                cms.Items.Add(new ToolStripSeparator());
                cms.Items.Add("Çıkış", null, (s, e) => {
                    try { config.FlushIfDirty(); } catch { }
                    SendGoodbye();
                    serial?.Dispose();
                    (log as IDisposable)?.Dispose();   // ILog dispose edilebilir olmak zorunda değil
                    trayIcon.Visible = false;
                    Application.Exit();
                });

                Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Suspend) { try { config.FlushIfDirty(); } catch { } SendGoodbye(); } };
                Microsoft.Win32.SystemEvents.SessionEnding += (s, e) => { try { config.FlushIfDirty(); } catch { } SendGoodbye(); };
                Microsoft.Win32.SystemEvents.SessionEnded += (s, e) => SendGoodbye();

                trayIcon = new NotifyIcon() { 
                    Icon = CreateIcon(), 
                    ContextMenuStrip = cms,
                    Text = "KontroXXL Tactical Dashboard",
                    Visible = true 
                };
                trayIcon.DoubleClick += (s, e) => ShowMainForm();

                // A8: v2'de tek 500ms timer her tick'te 8 TrueNAS isteği tetikliyordu.
                // Artık üç bağımsız periyot, hepsi config.json'dan ayarlanabilir.
                lcdTimer = new System.Windows.Forms.Timer { Interval = Math.Max(50, config.LcdIntervalMs) };
                lcdTimer.Tick += (s, e) => {
                    bool force = (DateTime.Now - lastHeartbeat).TotalSeconds > 4;
                    UpdateLCD(force);
                    if (force) lastHeartbeat = DateTime.Now;
                };
                lcdTimer.Start();

                pcTimer = new System.Windows.Forms.Timer { Interval = Math.Max(250, config.PcIntervalMs) };
                pcTimer.Tick += (s, e) => {
                    if (isPcUpdating) return;
                    isPcUpdating = true;
                    Task.Run(() => {
                        try { UpdatePcTelemetry(); }
                        catch (Exception ex) { log.Error("PC telemetri hatasi", ex); }
                        finally { isPcUpdating = false; }
                    });
                };
                pcTimer.Start();

                nasTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1000, config.NasIntervalMs) };
                nasTimer.Tick += (s, e) => {
                    if (isNasUpdating || !config.EnableNasModule || string.IsNullOrEmpty(config.TruenasIp)) return;
                    isNasUpdating = true;
                    Task.Run(async () => {
                        try { await UpdateNasTelemetry(); }
                        catch (Exception ex) { log.Error("NAS telemetri hatasi", ex); }
                        finally { isNasUpdating = false; }
                    });
                };
                nasTimer.Start();

                // A4: v2'de config.Save() yorum satırındaydı, Last* cache'i hiç diske inmiyordu.
                flushTimer = new System.Windows.Forms.Timer { Interval = Math.Max(5000, config.ConfigFlushIntervalMs) };
                flushTimer.Tick += (s, e) => {
                    try { config.FlushIfDirty(); }
                    catch (Exception ex) { log.Error("Config yazma hatasi", ex); }
                };
                flushTimer.Start();

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
            form.OnNasPower += (action) => _ = NasPower(action);
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
            if (mainForm.IsDisposed) { mainForm = new MainForm(config); mainForm.Secrets = secrets; _ = mainForm.Handle; WireFormEvents(mainForm); }
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

                serial?.Dispose();
                serial = null;
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

        private void InitSerial()
        {
            serial = new SerialLink(log,
                preferredPort: () => config.ArduinoPort,
                baud: () => config.ArduinoBaud,
                autoDetect: () => string.IsNullOrEmpty(config.ArduinoPort));

            serial.Connected += () => {
                lock (config.SyncRoot)
                {
                    if (config.ArduinoPort != serial.CurrentPort)
                    {
                        config.ArduinoPort = serial.CurrentPort;
                        config.MarkDirty();   // Task 10'un flush timer'i diske indirecek
                    }
                }
                SendData("ON");
                RunOnUi(() => UpdateLCD(forced: true));
                _ = PushArduinoData();
            };

            serial.LineReceived += line => {
                log.Debug("Arduino'dan gelen: " + line);
                if (line.StartsWith("EV:")) HandleArduinoEvent(line.Substring(3));
                else if (line == "CMD:READY" || line == "CMD:UPDATE") { RunOnUi(() => UpdateLCD(true)); _ = PushArduinoData(); }
                else if (line == "CMD:APPS" || line == "CMD:POOLS" || line == "CMD:SHORTCUTS") _ = PushArduinoData();
            };

            serial.Start();
        }

        private void UpdatePcTelemetry()
        {
            int cpu = (int)GetCpuUsage(), ram = GetRamUsage();
            double net = GetPcNetSpeed(), ghz = GetCpuSpeed();
            var gpu = GetGpuInfo();
            int temp = GetPcTemp();

            // A4: yazim ve serilestirme ayni kilidi paylasmak zorunda (Task 9).
            lock (config.SyncRoot)
            {
                config.LastCpu = cpu; config.LastRam = ram;
                config.LastNetSpeed = net; config.LastCpuFreq = ghz;
                config.LastGpu = gpu.Item1; config.LastGpuTemp = gpu.Item2; config.LastGpuFan = gpu.Item3;
                config.LastPcTemp = temp;
                config.MarkDirty();
            }

            if (mainForm != null && !mainForm.IsDisposed)
                mainForm.UpdateStats(cpu, ram, gpu.Item1, gpu.Item2, gpu.Item3, ghz, net);
        }

        private async Task UpdateNasTelemetry()
        {
            var nas = await GetTruenasData();

            if (nas.conn && !nasLastConn)
            {
                log.Info("NAS baglantisi saglandi, Arduino guncelleniyor.");
                await PushArduinoData();
            }
            nasLastConn = nas.conn;
            if (!nas.conn) return;

            // A4: yazim ve serilestirme ayni kilidi paylasmak zorunda (Task 9).
            lock (config.SyncRoot)
            {
                config.LastNasCpu = nas.nc; config.LastNasTemp = nas.nt;
                config.LastNasRx = nas.nrx; config.LastNasTx = nas.ntx;
                config.LastNasLoad = nas.nl; config.LastNasAlerts = nas.na;
                config.LastNasUptime = nas.up; config.LastNasMem = nas.mem;
                config.LastNasServicesJ = nas.svcs; config.LastNasAlertsJ = nas.alerts;
                config.LastPools = nas.pools; config.LastNasAppsJ = appsList;
                config.MarkDirty();
            }
        }

        private void HandleArduinoEvent(string ev)
        {
            LcdInput input;
            switch (ev)
            {
                case "UP":    input = LcdInput.Up; break;
                case "DN":    input = LcdInput.Down; break;
                case "CLICK": input = LcdInput.Click; break;
                case "BACK":  input = LcdInput.Back; break;
                default: return;
            }

            // Seri thread'inden geliyoruz; durum değişimini UI thread'ine sıraya al (A7).
            if (mainForm != null && mainForm.IsHandleCreated && !mainForm.IsDisposed)
                mainForm.BeginInvoke((MethodInvoker)(() => ApplyInput(input)));
            else
                ApplyInput(input);
        }

        private void ApplyInput(LcdInput input)
        {
            try
            {
                var data = BuildViewData();
                var previous = lcdState;
                var transition = LcdMenuModel.Apply(previous, input, data.Counts);
                lcdState = transition.State;

                // Mod veya sayfa değiştiyse ekranı temizle ve tam yeniden çizime zorla.
                if (lcdState.Mode != previous.Mode || lcdState.Page != previous.Page)
                {
                    SendData("CLR");
                    lastL0 = ""; lastL1 = "";
                    scrollOffset = 0;
                }

                RunEffect(transition.Effect, transition.EffectIndex);
                UpdateLCD(forced: true);
            }
            catch (Exception ex) { log.Error("ApplyInput hatasi", ex); }
        }

        /// <summary>
        /// LCD durumuna dokunan her sey UI thread'inde calismali (A7).
        /// Form yoksa (kapanis siralari) cagiran thread'de calistirilir — o noktada
        /// yarisacak bir timer da kalmamistir.
        /// </summary>
        private void RunOnUi(Action action)
        {
            var f = mainForm;
            if (f != null && f.IsHandleCreated && !f.IsDisposed)
            {
                try { f.BeginInvoke(action); return; }
                catch (Exception ex) { log.Debug("UI marshal basarisiz: " + ex.Message); }
            }
            action();
        }

        private void RunEffect(LcdEffect effect, int index)
        {
            switch (effect)
            {
                case LcdEffect.VolumeUp:   NudgeVolume(+2); break;
                case LcdEffect.VolumeDown: NudgeVolume(-2); break;
                case LcdEffect.RequestSync: _ = PushArduinoData(); break;
                case LcdEffect.ToggleApp:  ToggleApp(index); break;
                case LcdEffect.RunShortcut: RunShortcut(index); break;
                case LcdEffect.NasReboot:   _ = NasPower("REBOOT"); break;
                case LcdEffect.NasShutdown: _ = NasPower("SHUTDOWN"); break;
            }
        }

        private void NudgeVolume(int delta)
        {
            try
            {
                var dev = audioController?.DefaultPlaybackDevice;
                if (dev == null) return;
                dev.Volume = Math.Max(0, Math.Min(100, dev.Volume + delta));
                volumeShowUntil = DateTime.Now.AddSeconds(2);
            }
            catch (Exception ex) { log.Debug("Ses ayarlanamadi: " + ex.Message); }
        }

        private async Task NasPower(string action)
        {
            try
            {
                string endpoint = action == "REBOOT" ? "system/reboot" : "system/shutdown";
                log.Info($"NAS {action} komutu tetiklendi.");
                await httpClient.PostAsync($"https://{config.TruenasIp}/api/v2.0/{endpoint}", null);
            }
            catch (Exception ex) { log.Error($"NAS {action} hatasi", ex); }
        }

        /// <summary>Formatter ve durum makinesi için dünyanın anlık görüntüsü.</summary>
        private LcdViewData BuildViewData()
        {
            var apps = appsList;   // yerel kopya — arada değişse bile tutarlı okuruz
            var pools = poolsList;

            var appNames = new List<string>(apps.Count);
            var appRunning = new List<bool>(apps.Count);
            foreach (var a in apps)
            {
                appNames.Add(a["name"]?.ToString() ?? "");
                string st = a["state"]?.ToString();
                appRunning.Add(st == "RUNNING" || st == "ACTIVE");
            }

            var poolNames = new List<string>(pools.Count);
            var poolUsed = new List<int>(pools.Count);
            foreach (var p in pools)
            {
                poolNames.Add(p["name"]?.ToString() ?? "");
                poolUsed.Add((int)(p["used"] ?? 0));
            }

            var shortcutNames = new List<string>(config.Shortcuts.Count);
            foreach (var s in config.Shortcuts) shortcutNames.Add(s.Name ?? "");

            return new LcdViewData(
                config.LastCpu, config.LastCpuFreq, config.LastRam,
                config.LastGpu, config.LastGpuTemp, config.LastGpuFan, config.LastNetSpeed,
                config.LastNasCpu, config.LastNasTemp, config.LastNasRx, config.LastNasTx,
                config.LastNasAlerts, nasLastConn,
                appNames, appRunning, poolNames, poolUsed, shortcutNames);
        }

        private void UpdateLCD(bool forced = false)
        {
            if (serial == null || !serial.IsConnected) return;

            var now = DateTime.Now;
            if (forced && (now - lastLcdUpdate).TotalMilliseconds < 30) return;
            if (!forced && (now - lastLcdUpdate).TotalMilliseconds < 100) return;
            lastLcdUpdate = now;

            // Kaydırma sayaçları burada ilerler — formatter saf kalır.
            if ((now - lastScrollTick).TotalMilliseconds > 400) { scrollOffset++; lastScrollTick = now; }
            if (now >= _lcdTickerUntil) _lcdTickerText = "";
            else if ((now - _lastTickerScroll).TotalMilliseconds > 300) { _tickerScrollIdx++; _lastTickerScroll = now; }

            try
            {
                // M-2: CurrentVolume() bir AudioSwitcher COM ozellik okumasi; VolumeActive
                // degilse hic kimse VolumePercent'i kullanmiyor, o yuzden sadece gerektiginde oku.
                bool volumeActive = now < volumeShowUntil;
                var ctx = new LcdRenderContext(
                    Now: now,
                    ScrollOffset: scrollOffset,
                    VolumeActive: volumeActive,
                    VolumePercent: volumeActive ? CurrentVolume() : 0,
                    TickerText: string.IsNullOrEmpty(_lcdTickerText) ? null : _lcdTickerText,
                    TickerOffset: _tickerScrollIdx);

                var frame = LcdFormatter.Render(lcdState, BuildViewData(), ctx);

                if (forced || frame.Line0 != lastL0) { SendData("L0=" + frame.Line0); lastL0 = frame.Line0; }

                if (frame.BarValue.HasValue)
                {
                    string key = "BAR_" + frame.BarValue.Value;
                    if (forced || key != lastL1) { SendData("B1=" + frame.BarValue.Value); lastL1 = key; }
                }
                else if (forced || frame.Line1 != lastL1)
                {
                    SendData("L1=" + frame.Line1); lastL1 = frame.Line1;
                }
            }
            catch (Exception ex) { log.Error("UpdateLCD hatasi", ex); }
        }

        private int CurrentVolume()
        {
            try { return (int)(audioController?.DefaultPlaybackDevice?.Volume ?? 0); }
            catch { return 0; }
        }

        private void SendData(string msg) => serial?.Send(msg);

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
            if (idx < 0 || idx >= config.Shortcuts.Count) return;
            try {
                Process.Start(new ProcessStartInfo(config.Shortcuts[idx].Path) {
                    UseShellExecute = true, Arguments = config.Shortcuts[idx].Arguments });
            }
            catch (Exception ex) { log.Error("Kisayol calistirilamadi: " + config.Shortcuts[idx].Name, ex); }
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
                if (na > _prevNasAlertCount && na > 0)
                {
                    int count = na;
                    RunOnUi(() => {
                        _lcdTickerText = $"! YENI ALARM: {count} uyari aktif !  ";
                        _lcdTickerUntil = DateTime.Now.AddSeconds(10);
                        _tickerScrollIdx = 0;
                    });
                    log.Info($"LCD ticker tetiklendi: {count} yeni uyari.");
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
                var newPools = new JArray();
                foreach (var p in pools) {
                    long sz = (long)(p["size"] ?? 0L), usd = (long)(p["allocated"] ?? 0L);
                    newPools.Add(new JObject { ["name"] = p["name"], ["used"] = sz > 0 ? (int)(usd * 100 / sz) : 0 });
                }
                poolsList = newPools;   // tek atomik yayin — okuyucular yarim liste gormez

                RunOnUi(() => UpdateLCD(true));
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
