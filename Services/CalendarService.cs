// Services/CalendarService.cs
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using InfoKioskApp.Models;

namespace InfoKioskApp.Services
{
    public static class CalendarService
    {
        private static readonly string FilePath = "data/CalendarEvents.json";

        public static List<CalendarEvent> LoadEvents()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CalendarEvent>();

                var json = File.ReadAllText(FilePath);
                var list = JsonConvert.DeserializeObject<List<CalendarEvent>>(json);
                return list ?? new List<CalendarEvent>();
            }
            catch
            {
                return new List<CalendarEvent>();
            }
        }

        public static void SaveEvents(List<CalendarEvent> events)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? "data");
                var json = JsonConvert.SerializeObject(events, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // swallow for now — можно логировать в файл
            }
        }
    }
}
