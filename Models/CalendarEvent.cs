// Models/CalendarEvent.cs
using System;

namespace InfoKioskApp.Models
{
    public class CalendarEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today;
        public string Type { get; set; } = "Другое"; // holiday | vacation | weekend | other

        // helper: contains date
        public bool Covers(DateTime day)
        {
            var d = day.Date;
            return d >= StartDate.Date && d <= EndDate.Date;
        }
    }
}
