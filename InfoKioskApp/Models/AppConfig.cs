using System.Collections.Generic;

namespace InfoKioskApp.Models
{
    public class AppConfig
    {
        // === Интерфейс и кастомные разделы ===
        public InterfaceSettings InterfaceSettings { get; set; } = new InterfaceSettings();
        public List<CustomSection> CustomSections { get; set; } = [];

        // === Расписания ===
        public List<ScheduleItem> Schedules { get; set; } = [];

        public class ScheduleItem
        {
            public string Name { get; set; }
            public string FilePath { get; set; }
        }

        // === Пути к основным файлам ===
        public string MainSchedulePath { get; set; } = "data/MainSchedule.xlsx";
        public string ChangesPath { get; set; } = "data/Changes.xlsx";
        public string BellSchedulePath { get; set; } = "data/BellSchedule.json";
        public string MediaPath { get; set; } = "data/media";
        public string DocumentsPath { get; set; } = "data/documents";

        // === Настройки отображения ===
        public bool ShowChanges { get; set; } = true;
        public string ChangesType { get; set; } = "Table";
        public string Theme { get; set; } = "Dark";

        // === PIN и безопасность ===
        public string PinCode { get; set; } = "1234";

        
        // === Удалённое управление ===
        public bool RemoteAutoStart { get; set; } = true;
        public int RemotePort { get; set; } = 8080;

        // === Погода ===
        public string City { get; set; } = "Warsaw";
        public double CachedLatitude { get; set; }
        public double CachedLongitude { get; set; }
        public string OpenWeatherApiKey { get; set; } = "4fca50ecc7eb8fa8d8ae1021853e0e7c";


        // === Сайт ===
        public string WebsiteUrl { get; set; } = "https://obo-afan.gosuslugi.ru";
    }
}
