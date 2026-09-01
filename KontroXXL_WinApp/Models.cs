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
        
        public List<ShortcutItem> Shortcuts { get; set; } = new List<ShortcutItem>();

        // F20: Silently ignore stale/unknown fields from config.json (e.g. Truenas2Ip, LastNasServices)
        [JsonExtensionData]
        public IDictionary<string, JToken> _extra { get; set; }

        public static AppConfig Load()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(path))
            {
                try {
                    return JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
                } catch { }
            }
            return new AppConfig();
        }

        public void Save()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }

    public class ShortcutItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }
}
