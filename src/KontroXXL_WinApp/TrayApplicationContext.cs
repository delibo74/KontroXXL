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
using KontroXXL.Core.Diagnostics;
using KontroXXL.Core.Security;

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

        // LCD alert ticker + tepsi balonu (F4-1).
        // Sayac YOK: karar AlertNotificationPolicy'de, kimlik kumesi uzerinden veriliyor.
        // Bu alan yalnizca NAS poll thread'inden okunup yazilir (tek yazar).
        private AlertNotificationState _alertState = AlertNotificationState.Initial;
        private readonly AlertNotificationOptions _alertOptions = new AlertNotificationOptions();
        // F4-5: NAS anahtarinin son degerlendirmesi. NAS istekleri ve tepsi ipucu
        // bundan turetilir; anahtar bozuksa NAS modulu KENDI icinde susar, uygulama yasar.
        private bool nasKeyUsable;
        private string nasKeyMessage = "";

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

                // F4-5: NAS istemcisinin kurulumu KENDI icinde yasar. 2026-09-04'te
                // buradaki tek bir istisna (anahtarda satir sonu) butun kurucuyu
                // dusurmustu; tepsi ikonu, LCD ve Ayarlar da acilmamisti. Artik NAS
                // tarafi ne yaparsa yapsin uygulama ayaga kalkar.
                try { ApplyApiKey(); }
                catch (Exception ex)
                {
                    nasKeyUsable = false;
                    nasKeyMessage = ApiKeyPolicy.UnusableMessage;
                    log.Error("NAS modulu devre disi (API anahtari uygulanamadi)", ex);
                }

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
                cms.Items.Add("Güncellemeleri Denetle", null, async (s, e) => await CheckUpdatesAsync());
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
                // F4-1: balona tiklayan kullanici alarmi gormek istiyor, dashboard'u degil.
                trayIcon.BalloonTipClicked += (s, e) => { ShowMainForm(); mainForm.ShowNasTab(); };
                UpdateTrayText();

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
                    // Anahtar yoksa/bozuksa istek atmiyoruz: eskiden bu durum sessiz bir
                    // 401 dongusune donusuyordu (04:27 loglari). Durum tepsi ipucunda yazili.
                    if (isNasUpdating || !config.EnableNasModule || !nasKeyUsable || string.IsNullOrEmpty(config.TruenasIp)) return;
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
                StartSafeMode(ex);
            }
        }

        /// <summary>
        /// Kurucu yarida kaldiginda kullaniciyi ORTADA BIRAKMAYAN son care.
        /// </summary>
        /// <remarks>
        /// 2026-09-04: kurucu "KRITIK HATA" yazip sessizce bitiyordu; geriye tepsi ikonu
        /// olmayan, penceresi olmayan, ama yasayan bir surec kaliyordu — kullanici
        /// Ayarlar'a ulasip bozuk degeri duzeltemiyordu bile. Artik en azindan bir tepsi
        /// ikonu ve Ayarlar/Cikis yolu birakiyoruz. Buradaki her adim ayri korunuyor:
        /// kurtarma denemesi kendisi yeni bir istisna uretmemeli.
        /// </remarks>
        private void StartSafeMode(Exception cause)
        {
            try
            {
                if (mainForm == null && config != null)
                {
                    try
                    {
                        mainForm = new MainForm(config);
                        mainForm.Secrets = secrets;
                        _ = mainForm.Handle;
                    }
                    catch (Exception ex) { log.Error("Guvenli mod: pencere acilamadi", ex); }
                }

                var cms = new ContextMenuStrip();
                if (mainForm != null) cms.Items.Add("Ayarlari Ac", null, (s, e) => { try { ShowMainForm(); } catch { } });
                cms.Items.Add("Cikis", null, (s, e) => { try { trayIcon.Visible = false; } catch { } Application.Exit(); });

                if (trayIcon == null)
                {
                    trayIcon = new NotifyIcon { Icon = CreateIcon(), Visible = true };
                    if (mainForm != null) trayIcon.DoubleClick += (s, e) => { try { ShowMainForm(); } catch { } };
                }
                trayIcon.ContextMenuStrip = cms;
                trayIcon.Text = "KontroXXL — guvenli mod (Ayarlar)";
                trayIcon.Visible = true;

                trayIcon.BalloonTipTitle = "KontroXXL guvenli modda";
                trayIcon.BalloonTipText = "Baslangic tamamlanamadi: " + cause.Message +
                    "\nAyarlari acip degerleri duzeltin.";
                trayIcon.ShowBalloonTip(10000);

                Log("Guvenli mod: tepsi ikonu ve Ayarlar erisilebilir.");
            }
            catch (Exception ex)
            {
                Log("Guvenli mod da kurulamadi: " + ex.Message);
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

        // Velopack GithubSource'un bekledigi sey DEPO adresidir, releases sayfasi ya da
        // atom/JSON feed'i DEGIL: kendisi bundan api.github.com tabanini turetiyor.
        // ".../releases" ya da ".git" ekli bir deger verilirse API cagrisi 404 doner.
        // accessToken null geciliyor (asagida GithubSource'a), prerelease kapali.
        // F4-3 OLCUMU: depo bir sure PRIVATE'ti ve o sirada kimliksiz istemci
        // release'leri GOREMIYORDU. 2026-09-04 05:09'da `gh repo view` ile yeniden
        // olculdu: visibility = PUBLIC, feed anonim okunabiliyor. Depo tekrar private
        // yapilirsa guncelleme denetimi calismayi birakir (ya da buraya gercek bir
        // token verilmesi gerekir); bos birakilirsa menu bunu ACIKCA soyler.
        private const string UpdateFeedUrl = "https://github.com/delibo74/KontroXXL";

        // Menu ogesi tekrar tekrar tiklanabilir; indirme suruyorken ikinci bir
        // UpdateManager acmak ayni dosyalari ustuste yazardi. Yalnizca UI thread'inden
        // okunup yazildigi icin kilide gerek yok.
        private bool updateCheckRunning;

        // Yikim (config flush + LCD vedasi + seri port + tepsi ikonu) islendikten SONRA
        // true olur. ApplyUpdatesAndRestart o noktadan sonra firlarsa surec artik
        // onarilamaz; catch bunu gorup kapanmayi secer.
        private bool updateTornDown;

        private async Task CheckUpdatesAsync()
        {
            if (updateCheckRunning)
            {
                // Spec 9: sessiz basarisizlik yasak. Menuye ikinci kez basan kullanici
                // hicbir sey olmadigini gorurse denetimin hic calismadigini sanir.
                MessageBox.Show("Güncelleme denetimi zaten sürüyor.", "KontroXXL",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            updateCheckRunning = true;
            try
            {
                if (string.IsNullOrWhiteSpace(UpdateFeedUrl))
                {
                    MessageBox.Show("Güncelleme kaynağı yapılandırılmamış.", "KontroXXL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var mgr = new Velopack.UpdateManager(
                    new Velopack.Sources.GithubSource(UpdateFeedUrl, null, false));

                // Kurulmamis (portable/derleme dizininden calisan) bir kopyada
                // ApplyUpdatesAndRestart firlatir. Once soyle, sonra deneme.
                if (!mgr.IsInstalled)
                {
                    MessageBox.Show("Bu kopya kurulum paketiyle kurulmamış; güncelleme yapılamaz.",
                        "KontroXXL", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var newVer = await mgr.CheckForUpdatesAsync();
                if (newVer == null)
                {
                    MessageBox.Show("Zaten güncelsiniz.", "KontroXXL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show($"Yeni sürüm: {newVer.TargetFullRelease.Version}\nŞimdi güncellensin mi?",
                        "KontroXXL", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                await mgr.DownloadUpdatesAsync(newVer);

                // Yeniden baslatma bu process'i oldurur: cikis yolunun yaptigi her seyi
                // once burada yapmak gerekiyor — kirli config diske insin, LCD veda yazsin,
                // COM portu birakilsin (acik kalirsa yeni process porta baglanamaz).
                // Bu noktadan sonra surec onarilamaz duruma girer: bayrak, asagidaki
                // catch'in "devam et" ile "kapan" arasinda dogru secimi yapmasini saglar.
                updateTornDown = true;
                try { config.FlushIfDirty(); } catch (Exception ex) { log.Error("Guncelleme oncesi config yazilamadi", ex); }
                SendGoodbye();
                serial?.Dispose();
                if (trayIcon != null) trayIcon.Visible = false;

                log.Info("Guncelleme uygulaniyor: " + newVer.TargetFullRelease.Version);
                mgr.ApplyUpdatesAndRestart(newVer.TargetFullRelease);
            }
            catch (Exception ex)
            {
                // Sessiz basarisizlik yasak (spec 9): kullanici "Denetle"ye bastiginda
                // hicbir sey olmamasi, guncelleme yok sanmasina yol acar. Sessiz-BOZUK
                // durum ise daha kotusu: yikim islendikten SONRA hata alirsak, uyari
                // gosterip devam etmek tepsisi gizli, seri portu kapali, LCD'sinde
                // "BYE BYE" yazan ve kullanicinin menuye ulasamadigi bir hayalet surec
                // birakirdi. Yikim geri alinamaz; tek dogru davranis: soyle ve kapan.
                log.Error("Guncelleme denetimi hatasi", ex);
                var response = UpdateFailurePolicy.Describe(updateTornDown, ex.Message);

                if (response.MustExit && trayIcon != null)
                {
                    // Seri port ve LCD geri getirilemez, ama ikonu geri koymak kullaniciya
                    // kapanana kadar gorunur bir uygulama birakir (gizli/tikleyen surec degil).
                    trayIcon.Visible = true;
                }

                MessageBox.Show(response.Message, "KontroXXL", MessageBoxButtons.OK,
                    response.MustExit ? MessageBoxIcon.Error : MessageBoxIcon.Warning);

                if (response.MustExit)
                {
                    Application.Exit();
                }
            }
            finally { updateCheckRunning = false; }
        }

        private void ShowMainForm()
        {
            if (mainForm.IsDisposed) { mainForm = new MainForm(config); mainForm.Secrets = secrets; _ = mainForm.Handle; WireFormEvents(mainForm); }
            mainForm.Show();
            mainForm.BringToFront();
        }

        /// <summary>
        /// config'teki anahtari normalize edip Authorization basligina uygular.
        /// ATMAZ: gecersiz bir anahtar yalnizca NAS modulunu susturur.
        /// </summary>
        private void ApplyApiKey()
        {
            var evaluation = ApiKeyPolicy.Evaluate(config.TruenasApiKey);

            // Onarilmis anahtari bellege de geri yaz: bir daha ayni degeri her
            // aciliste temizlemek zorunda kalmayalim, kaydedildiginde duzelmis olsun.
            if (evaluation.Status == ApiKeyStatus.Repaired)
            {
                lock (config.SyncRoot) { config.TruenasApiKey = evaluation.Key; }
                config.MarkDirty();
            }

            nasKeyUsable = evaluation.IsUsable;
            nasKeyMessage = evaluation.Message;

            httpClient.DefaultRequestHeaders.Authorization = evaluation.IsUsable
                ? new AuthenticationHeaderValue("Bearer", evaluation.Key)
                : null;

            if (evaluation.Message.Length > 0) log.Info(evaluation.Message);
            UpdateTrayText();
        }

        /// <summary>Tepsi ipucu: NAS anahtari yok/bozuksa kullanici bunu GORSUN.</summary>
        private void UpdateTrayText()
        {
            if (trayIcon == null) return;
            try
            {
                // NotifyIcon.Text 63 karakterle sinirli; asilirsa ArgumentException atar.
                string text = (config.EnableNasModule && !nasKeyUsable)
                    ? "KontroXXL — NAS anahtari yok/gecersiz (Ayarlar)"
                    : "KontroXXL Tactical Dashboard";
                trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
            catch { }
        }

        private void Reload()
        {
            Log("Ayarlar yeniden yukleniyor...");
            try {
                ApplyApiKey();

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
                autoDetect: () => config.AutoDetectPort);

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

                // F4-1: yeni alarm karari. Eski "na > _prevNasAlertCount" testi sayi
                // tabanliydi (takas edilen alarmi kaciriyordu), level'i hic okumuyordu
                // ve her acilista mevcut alarmlari "yeni" sayiyordu — ucu de politikada
                // kapandi. Burada yalnizca gosterim var.
                var decision = AlertNotificationPolicy.Decide(
                    _alertState, ToNasAlerts(alertsArr), _alertOptions, DateTimeOffset.Now);
                _alertState = decision.NextState;

                if (decision.ShouldNotify)
                {
                    int count = decision.NewAlertCount;
                    string balloonTitle = decision.Title;
                    string balloonBody = decision.Body;
                    bool balloonEnabled = config.NotifyOnNasAlerts;

                    RunOnUi(() => {
                        // LCD yolu Sanitize'li kalir (7 bit ekran), balon Unicode tasir.
                        _lcdTickerText = $"! YENI ALARM: {count} uyari !  ";
                        _lcdTickerUntil = DateTime.Now.AddSeconds(10);
                        _tickerScrollIdx = 0;

                        if (balloonEnabled && trayIcon != null && trayIcon.Visible)
                        {
                            try
                            {
                                trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
                                trayIcon.BalloonTipTitle = balloonTitle;
                                trayIcon.BalloonTipText = balloonBody;
                                trayIcon.ShowBalloonTip(10000);
                                // Spec 9: Win10/11 balonu toast'a yonlendirir ve kullanicinin
                                // "bildirimleri kapat" ayari onu SESSIZCE yutabilir. Gonderdigimizi
                                // log'a yazmazsak "bildirim gelmedi" sikayeti teshis edilemez.
                                log.Info($"Tepsi balonu gonderildi: {count} yeni uyari.");
                            }
                            catch (Exception bex) { log.Error("Tepsi balonu gosterilemedi", bex); }
                        }
                    });

                    log.Info($"LCD ticker tetiklendi: {count} yeni uyari.");
                }

                return (nc, nrx, ntx, nl, nt, na, uptime, memory, svcsArr, true, poolArr, alertsArr);
            } catch (Exception ex) { Log("GetTruenasData error: " + ex.Message); return (0,0,0,0,0,0, "","", new JArray(), false, new JArray(), new JArray()); }
        }

        /// <summary>
        /// F4-1: TrueNAS <c>alert/list</c> ogelerini Core'un tanidigi sade kayda cevirir.
        /// Core'un Newtonsoft referansi yok (ArchitectureTests'teki katmanlama), donusum
        /// bu yuzden UI tarafinda. Tek bir bozuk oge tum tick'i dusurmemeli — her alan
        /// ayri ayri savunuluyor.
        /// </summary>
        private static IReadOnlyList<NasAlert> ToNasAlerts(JArray alerts)
        {
            var list = new List<NasAlert>();
            if (alerts == null) return list;

            foreach (var a in alerts)
            {
                try
                {
                    string id = (string)a["id"];
                    string level = (string)a["level"];
                    // TrueNAS'ta gosterilecek metin "formatted"; eski/kisitli yanitlarda
                    // "text", o da yoksa alarm sinifi ("klass") hic yoktan iyidir.
                    string text = (string)(a["formatted"] ?? a["text"] ?? a["klass"]);
                    list.Add(new NasAlert(id, level, text));
                }
                catch { /* tek oge okunamadi; digerleri islenmeye devam etsin */ }
            }
            return list;
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
