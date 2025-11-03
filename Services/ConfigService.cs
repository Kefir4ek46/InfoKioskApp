using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace InfoKioskApp.Services
{
    public static class ConfigService
    {
        private static readonly string ConfigPath = "data\\config.json";

        public static AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonConvert.DeserializeObject<AppConfig>(json);
                }
                else
                {
                    var defaultConfig = new AppConfig();
                    SaveConfig(defaultConfig);
                    return defaultConfig;
                }
            }
            catch (Exception)
            {
                return new AppConfig();
            }
        }

        public static void SaveConfig(AppConfig config)
        {
            Directory.CreateDirectory("data");
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
    }

    public class AppConfig
    {
        // --- Интерфейс ---
        public string Theme { get; set; } = "dark";
        public string FontFamily { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 14;

        // --- Расписания ---
        public string MainSchedulePath { get; set; } = "data/MainSchedule.xlsx";
        public bool ShowChanges { get; set; } = true;
        public string ChangesPath { get; set; } = "data/ExtraSchedule.xlsx";
        public string ChangesType { get; set; } = "excel";
        public List<ExtraSchedule> ExtraSchedules { get; set; } = new List<ExtraSchedule>();

        // --- Погода ---
        public bool ShowWeather { get; set; } = true;
        public string WeatherApiKey { get; set; } = "";
        public string WeatherCityId { get; set; } = "524901"; // например, Москва


        public string PinCode { get; set; } = "1111";

        
    }

    public class ExtraSchedule
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Type { get; set; } = "excel"; // excel | image | pdf
    }
}
