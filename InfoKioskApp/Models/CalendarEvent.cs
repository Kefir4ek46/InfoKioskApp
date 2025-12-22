using System;
using Newtonsoft.Json;

namespace InfoKioskApp.Models
{
    public class CalendarEvent
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("title")]
        public string Title { get; set; } = "";

        [JsonProperty("startDate")]
        public DateTime StartDate { get; set; } = DateTime.Today;

        [JsonProperty("endDate")]
        public DateTime EndDate { get; set; } = DateTime.Today;

        [JsonProperty("type")]
        public string Type { get; set; } = "Другое"; // "Каникулы","Праздник","Выходной","Другое"

        // helper: contains date — метод, атрибуты ему не нужны (и ставить JsonIgnore здесь нельзя)
        public bool Covers(DateTime day)
        {
            var d = day.Date;
            return d >= StartDate.Date && d <= EndDate.Date;
        }
    }
}

