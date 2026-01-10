using System;
using System.IO;
using InfoKioskApp.Models;
using Newtonsoft.Json;

namespace InfoKioskApp.Services
{
    public static class ConfigService
    {
        private static readonly string ConfigPath = "data/config.json";

        // 🔹 текущий конфиг в памяти
        public static AppConfig Current { get; private set; } = new AppConfig();

        // 🔔 событие изменения конфига
        public static event Action<AppConfig>? ConfigChanged;

        public static AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    Current = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    Current = new AppConfig();
                }
            }
            catch
            {
                Current = new AppConfig();
            }

            return Current;
        }

        public static void SaveConfig(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory("data");
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);

                // обновляем текущий конфиг
                Current = config;

                // 🔥 уведомляем все подписанные части приложения
                ConfigChanged?.Invoke(Current);
            }
            catch
            {
                // лог при желании
            }
        }
    }
}
