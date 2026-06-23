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

        /// <summary>
        /// Описание события (для мероприятий — детали, место, время и т.п.).
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; } = "";

        /// <summary>
        /// Ежегодное событие: если true, событие повторяется каждый год
        /// в те же месяц+день (для StartDate и EndDate).
        /// Полезно для праздников (День знаний 1 сентября, День учителя и т.п.).
        /// </summary>
        [JsonProperty("yearly")]
        public bool Yearly { get; set; } = false;

        // helper: contains date — метод, атрибуты ему не нужны (и ставить JsonIgnore здесь нельзя)
        public bool Covers(DateTime day)
        {
            var d = day.Date;

            if (Yearly)
            {
                // Для ежегодных событий сравниваем только месяц+день.
                // Учитываем переход через декабрь→январь (если start > end по месяцу,
                // считаем что событие захватывает конец года + начало следующего).
                int dayOfYear = d.DayOfYear;
                int startDay = StartDate.DayOfYear;
                int endDay = EndDate.DayOfYear;

                if (startDay <= endDay)
                {
                    return dayOfYear >= startDay && dayOfYear <= endDay;
                }
                else
                {
                    // Событие захватывает конец года + начало следующего.
                    return dayOfYear >= startDay || dayOfYear <= endDay;
                }
            }

            return d >= StartDate.Date && d <= EndDate.Date;
        }

        /// <summary>
        /// Возвращает "эффективную" дату начала для отображения в конкретном году.
        /// Для ежегодных событий подставляет год текущего (или ближайшего) вхождения.
        /// </summary>
        public DateTime GetEffectiveStartDate(int year)
        {
            if (!Yearly) return StartDate;
            return new DateTime(year, StartDate.Month, StartDate.Day);
        }

        public DateTime GetEffectiveEndDate(int year)
        {
            if (!Yearly) return EndDate;
            return new DateTime(year, EndDate.Month, EndDate.Day);
        }
    }
}

