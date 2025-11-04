using InfoKioskApp.Models;
using System.Collections.Generic;


namespace InfoKioskApp
{
    

    public class AppConfig
    {
        public InterfaceSettings InterfaceSettings { get; set; } = new InterfaceSettings();
        public List<CustomSection> CustomSections { get; set; } = new List<CustomSection>();

        // === Основные пути ===
        public string MainSchedulePath { get; set; } = "data/MainSchedule.xlsx";
        public string ChangesPath { get; set; } = "data/Changes.xlsx";
        public bool ShowChanges { get; set; } = true;
        public string ChangesType { get; set; } = "Table";


        // === Дополнительные расписания ===
        public List<ExtraSchedule> ExtraSchedules { get; set; } = new List<ExtraSchedule>();

        // === Тип отображения изменений ===
        public string PinCode { get; set; } = "1234";


        // === Остальные настройки ===
        public string MediaPath { get; set; } = "data/media";
        public string DocumentsPath { get; set; } = "data/documents";
        public string BellSchedulePath { get; set; } = "data/BellSchedule.json";

        public string Theme { get; set; } = "Dark";

        // === Кэш координат для погоды ===
        public string City { get; set; } = "Warsaw";
        public double CachedLatitude { get; set; }
        public double CachedLongitude { get; set; }

        public string OpenWeatherApiKey { get; set; } = "4fca50ecc7eb8fa8d8ae1021853e0e7c";
    }
}
