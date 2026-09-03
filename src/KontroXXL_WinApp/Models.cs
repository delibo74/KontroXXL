using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace KontroXXL_WinApp
{
    /// <summary>A5 fix round 1: ApplyProtection'in sonucu — cagiran taraf "Failed"i
    /// ayirt edip eski blobu korumali, sessizce bos deger yazmamali.</summary>
    public enum SecretApplyResult { Applied, Unchanged, Failed }

    public class AppConfig
    {
        // Faz 2: yapilandirma semasi surumu. 3 = %APPDATA% donemi.
        public int SchemaVersion { get; set; } = 3;

        public string ArduinoPort { get; set; } = "";

        // A9: v2'de "COM4" degeri sihirli sekilde "otomatik algila" demekti.
        // Gercekten COM4'teki cihazi olan kullanici surekli eziliyordu.
        // Diske yazilan hali. null = Faz 2 oncesi dosya, deger hic belirtilmemis.
        [JsonProperty("AutoDetectPort")]
        public bool? AutoDetectPortRaw { get; set; }

        /// <summary>Tuketiciler icin non-null gorunum. Varsayilan: otomatik algila.</summary>
        [JsonIgnore]
        public bool AutoDetectPort
        {
            get => AutoDetectPortRaw ?? true;
            set => AutoDetectPortRaw = value;
        }

        public int ArduinoBaud { get; set; } = 115200;
        public string TruenasIp { get; set; } = "";

        // A5: diske SIFRELI yazilir. Duz metin yalnizca bellekte tutulur.
        public string TruenasApiKeyProtected { get; set; } = "";

        [JsonIgnore] public string TruenasApiKey { get; set; } = "";

        /// <summary>Cozulemeyen bir anahtar vardi — kullaniciya bildirilmeli.</summary>
        [JsonIgnore] public bool SecretUnreadable { get; private set; }

        public bool EnableNasModule { get; set; } = true;
        public bool EnableArduinoModule { get; set; } = true;
        public bool EnableShortcutsModule { get; set; } = true;

        // Cache for values to prevent empty UI on startup
        public int LastNasCpu { get; set; } = 0;
        public int LastNasTemp { get; set; } = 0;
        public int LastNasAlerts { get; set; } = 0;
        public double LastNasLoad { get; set; } = 0;
        public double LastNasRx { get; set; } = 0;
        public double LastNasTx { get; set; } = 0;
        public string LastNasUptime { get; set; } = "";
        public string LastNasMem { get; set; } = "";
        public JArray LastNasServicesJ { get; set; } = new JArray();
        public JArray LastNasAlertsJ { get; set; } = new JArray();
        public JArray LastNasAppsJ { get; set; } = new JArray();

        public int LastCpu { get; set; } = 0;
        public int LastRam { get; set; } = 0;
        public double LastNetSpeed { get; set; } = 0;
        public double LastCpuFreq { get; set; } = 0;
        public int LastGpu { get; set; } = 0;
        public int LastGpuTemp { get; set; } = 0;
        public int LastGpuFan { get; set; } = 0;
        public int LastPcTemp { get; set; } = 0;

        public JArray LastPools { get; set; } = new JArray();

        // Faz 1 (A8): tek 500ms timer yerine ayrı periyotlar
        public int LcdIntervalMs { get; set; } = 200;
        public int PcIntervalMs { get; set; } = 1000;
        public int NasIntervalMs { get; set; } = 5000;
        public int ConfigFlushIntervalMs { get; set; } = 30000;

        public List<ShortcutItem> Shortcuts { get; set; } = new List<ShortcutItem>();

        // F20: Silently ignore stale/unknown fields from config.json (e.g. Truenas2Ip, LastNasServices)
        [JsonExtensionData]
        public IDictionary<string, JToken> _extra { get; set; }

        // A8/A4: telemetri her saniye diske yazılmaz; kirli işaretlenir, flush timer'ı indirir.
        [JsonIgnore] private bool _dirty;
        [JsonIgnore] public string SourcePath { get; private set; } = "";

        // Task 10 arka plan thread'inden Last* alanlarini yazacak, UI timer'i ise
        // FlushIfDirty() cagiracak. Serilestirme tum nesne grafigini gezdigi icin
        // (JArray uyeleri dahil) yazim ve serilestirme ayni kilidi paylasmak zorunda.
        [JsonIgnore] public object SyncRoot { get; } = new object();

        public void MarkDirty() { lock (SyncRoot) { _dirty = true; } }

        // C-1: yukleme basarisiz olduysa elimizdeki nesne kullanicinin gercek
        // ayarlari DEGIL, bos bir varsayilan. Otomatik yazim onlari kalici siler.
        [JsonIgnore] public bool LoadFailed { get; private set; }

        public static AppConfig Load(string path)
        {
            bool existed = File.Exists(path);
            string raw = KontroXXL.Core.Configuration.JsonFileStore.ReadOrNull(path);

            // Dosya var ama okunamadi -> basarisizlik. Dosya hic yok -> ilk calistirma, normal.
            bool failed = existed && raw == null;

            AppConfig cfg = null;
            if (raw != null)
            {
                try { cfg = JsonConvert.DeserializeObject<AppConfig>(raw); } catch { failed = true; }
                if (cfg == null) failed = true;   // bos ya da "null" iceren dosya
            }

            cfg ??= new AppConfig();
            cfg.SourcePath = path;
            cfg.LoadFailed = failed;

            // A9 gocu (tek seferlik): anahtar yoksa bu Faz 2 oncesi bir dosya.
            // v2'nin kuralini birebir uygula — port bos ya da "COM4" ise o zaman da
            // otomatik algilaniyordu; baska bir deger ise kullanicinin acik secimiydi
            // ve ona DOKUNMAYIZ. Bu, "COM4" literalinin kodda kalan tek mesru kullanimi:
            // eski dosyalari okumak icin, yeni karar vermek icin degil.
            // Sadece basarili yuklemede calisir — LoadFailed durumunda korunacak bir
            // kullanici niyeti yok, duz varsayilan (true) kalir.
            if (!failed && cfg.AutoDetectPortRaw == null)
                cfg.AutoDetectPort = string.IsNullOrEmpty(cfg.ArduinoPort) || cfg.ArduinoPort == "COM4";

            return cfg;
        }

        public void FlushIfDirty()
        {
            // C-1: bozuk yuklemeden sonra otomatik yazim kullanicinin dosyasini yok eder.
            // Yalnizca kullanicinin bilincli "Kaydet"i uzerine yazabilir.
            if (LoadFailed) return;
            if (_dirty) Save();
        }

        /// <summary>Yuklemeden sonra: sifreliyi coz, eski duz metni goc ettir.
        /// Fix round 1: alan atamalari SyncRoot altinda; protector cagrilari kilit disinda.</summary>
        public bool UnprotectSecrets(KontroXXL.Core.Security.ISecretProtector protector)
        {
            bool changed = false;
            bool unreadable = false;

            // Faz 1 oncesi dosyalarda anahtar duz metin "TruenasApiKey" alanindaydi;
            // artik [JsonIgnore] oldugu icin _extra'ya dusuyor. Oradan al ve sifrele.
            if (_extra != null && _extra.TryGetValue("TruenasApiKey", out var legacy))
            {
                string plain = legacy?.ToString() ?? "";
                _extra.Remove("TruenasApiKey");
                if (!string.IsNullOrEmpty(plain))
                {
                    string cipher = protector.Protect(plain);
                    lock (SyncRoot)
                    {
                        TruenasApiKey = plain;
                        TruenasApiKeyProtected = cipher;
                    }
                    changed = true;
                }
            }
            else if (!string.IsNullOrEmpty(TruenasApiKeyProtected))
            {
                string plain = protector.Unprotect(TruenasApiKeyProtected);
                if (plain == null)
                {
                    unreadable = true;
                    lock (SyncRoot) { TruenasApiKey = ""; }
                }
                else
                {
                    lock (SyncRoot) { TruenasApiKey = plain; }
                }
            }

            lock (SyncRoot) { SecretUnreadable = unreadable; }

            return changed;
        }

        /// <summary>
        /// Kaydetmeden once bellekteki duz metni sifreli alana yansitir.
        /// KURAL: mevcut blob YALNIZCA yerine gecerli bir yenisi konabiliyorsa degistirilir.
        /// Cozulemeyen bir anahtar varken bos deger yazmak, kurtarilabilir veriyi yok eder.
        /// </summary>
        public SecretApplyResult ApplyProtection(KontroXXL.Core.Security.ISecretProtector protector)
        {
            string plain = TruenasApiKey ?? "";

            if (plain.Length == 0)
            {
                // Cozulemeyen bir blob duruyor ve kullanici yeni bir sey yazmadi -> DOKUNMA.
                if (SecretUnreadable) return SecretApplyResult.Unchanged;

                // Kullanici alani bilerek bosaltti -> temizle.
                lock (SyncRoot) { TruenasApiKeyProtected = ""; }
                return SecretApplyResult.Applied;
            }

            // Sifreleme kilit DISINDA yapilir; kilit yalnizca atama icin alinir.
            string cipher = protector.Protect(plain);
            if (string.IsNullOrEmpty(cipher))
                return SecretApplyResult.Failed;   // eski blob KORUNUR

            lock (SyncRoot)
            {
                TruenasApiKeyProtected = cipher;
                SecretUnreadable = false;          // artik okunabilir bir anahtarimiz var
            }
            return SecretApplyResult.Applied;
        }

        public void Save()
        {
            // C-1: okunamayan dosyanin uzerine yazmadan once kenara al — geri donulebilir olsun.
            if (LoadFailed)
            {
                try
                {
                    if (File.Exists(SourcePath))
                        File.Move(SourcePath,
                                  SourcePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                                  overwrite: false);
                }
                catch { }
                LoadFailed = false;
            }

            string json;
            lock (SyncRoot)
            {
                json = JsonConvert.SerializeObject(this, Formatting.Indented);
                // D1: bayragi anlik goruntuyle AYNI kilitte temizle. Ayri bir kilit
                // aliminda temizlemek, disk I/O penceresinde gelen MarkDirty()'yi eziyordu.
                _dirty = false;
            }

            try { KontroXXL.Core.Configuration.JsonFileStore.WriteAtomic(SourcePath, json); }
            catch { lock (SyncRoot) { _dirty = true; } throw; }   // yazim patlarsa flush kaybolmasin
        }
    }

    public class ShortcutItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }
}
