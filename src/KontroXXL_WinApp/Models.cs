using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace KontroXXL_WinApp
{
    public class AppConfig
    {
        public string ArduinoPort { get; set; } = "COM4";
        public int ArduinoBaud { get; set; } = 115200;
        public string TruenasIp { get; set; } = "";
        public string TruenasApiKey { get; set; } = "";
        
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
        [JsonIgnore] public string SourcePath { get; private set; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public void MarkDirty() => _dirty = true;

        public static AppConfig Load(string path = null)
        {
            path ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            string raw = KontroXXL.Core.Configuration.JsonFileStore.ReadOrNull(path);
            AppConfig cfg = null;
            if (raw != null)
            {
                try { cfg = JsonConvert.DeserializeObject<AppConfig>(raw); } catch { }
            }
            cfg ??= new AppConfig();
            cfg.SourcePath = path;
            return cfg;
        }

        public void Save()
        {
            KontroXXL.Core.Configuration.JsonFileStore.WriteAtomic(
                SourcePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            _dirty = false;
        }

        public void FlushIfDirty()
        {
            if (_dirty) Save();
        }
    }

    public class ShortcutItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }
}
