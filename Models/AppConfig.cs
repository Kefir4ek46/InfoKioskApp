using InfoKioskApp.Models;
using System.Collections.Generic;


namespace InfoKioskApp
{
    

    public class AppConfig
    {
        public InterfaceSettings InterfaceSettings { get; set; } = new InterfaceSettings();
        public List<CustomSection> CustomSections { get; set; } = new List<CustomSection>();
        public List<ScheduleFile> Schedules { get; set; } = new List<ScheduleFile>();


        // === Основные пути ===
        public string MainSchedulePath { get; set; } = "data/MainSchedule.xlsx";
        public string ChangesPath { get; set; } = "data/Changes.xlsx";
        public bool ShowChanges { get; set; } = true;
        public string ChangesType { get; set; } = "Table";



        // === Дополнительные расписания ===
        public List<ScheduleFile> ExtraSchedules { get; set; } = new List<ScheduleFile>();

        // === Тип отображения изменений ===
        public string PinCode { get; set; } = "1234";


        // === Остальные настройки ===
        public int MediaAutoIntervalSeconds { get; set; } = 15; // интервал в секундах
        public string MediaPath { get; set; } = "data/media";
        public string DocumentsPath { get; set; } = "data/documents";
        public string BellSchedulePath { get; set; } = "data/BellSchedule.json";
        public bool RemoteAutoStart { get; set; } = true;
        public int RemotePort { get; set; } = 8080;


        public string Theme { get; set; } = "Dark";

        // === Кэш координат для погоды ===
        public string City { get; set; } = "Warsaw";
        public double CachedLatitude { get; set; }
        public double CachedLongitude { get; set; }

        public string OpenWeatherApiKey { get; set; } = "4fca50ecc7eb8fa8d8ae1021853e0e7c";
    }
}
