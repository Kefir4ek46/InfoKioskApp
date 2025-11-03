using System;
using System.IO;
using Newtonsoft.Json;

namespace InfoKioskApp.Services
{
    public static class ConfigService
    {
        private static readonly string ConfigPath = "data/config.json";

        public static AppConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new AppConfig();

                string json = File.ReadAllText(ConfigPath);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                return config ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void SaveConfig(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory("data");
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
