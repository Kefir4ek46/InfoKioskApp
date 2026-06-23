using System;
using System.IO;
using InfoKioskApp.Models;
using Newtonsoft.Json;

namespace InfoKioskApp.Services
{
    public static class ConfigService
    {
        // Сначала ищем config.json рядом с исполняемым файлом (AppDomain.BaseDirectory),
        // затем — в текущей рабочей директории. Это спасает и при запуске из Visual Studio
        // (current dir = папка проекта, где data/config.json в исходниках), и при запуске
        // собранного .exe из bin/Release (BaseDirectory/data/config.json — копируется csproj'ом).
        private static string ConfigPath =>
            File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "config.json"))
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "config.json")
                : "data/config.json";

        private static string ConfigDir =>
            File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "config.json"))
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data")
                : "data";

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
                Directory.CreateDirectory(ConfigDir);
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
