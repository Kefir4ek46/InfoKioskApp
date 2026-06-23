namespace InfoKioskApp.Models
{
    /// <summary>
    /// Расширенная модель погоды — содержит все ключевые параметры,
    /// которые отображаются как в шапке киоска, так и в подробной панели.
    /// </summary>
    public class WeatherData
    {
        /// <summary>Название города, возвращённое OpenWeather.</summary>
        public string City { get; set; }

        /// <summary>Температура в °C (units=metric).</summary>
        public double Temperature { get; set; }

        /// <summary>Ощущается как (°C).</summary>
        public double FeelsLike { get; set; }

        /// <summary>Краткое текстовое описание ("облачно", "ясно" и т.д.).</summary>
        public string Description { get; set; }

        public string IconCode { get; set; }
        public string IconUrl => $"https://openweathermap.org/img/wn/{IconCode}@2x.png";

        /// <summary>Влажность, %.</summary>
        public int Humidity { get; set; }

        /// <summary>Атмосферное давление в гПа (как отдаёт OpenWeather).</summary>
        public int PressureHpa { get; set; }

        /// <summary>Давление в мм рт. ст. — привычная единица для русскоязычного UI.</summary>
        public int PressureMmHg => (int)System.Math.Round(PressureHpa * 0.75006375541921);

        /// <summary>Скорость ветра, м/с.</summary>
        public double WindSpeed { get; set; }

        /// <summary>Направление ветра в градусах (0 = север, 90 = восток).</summary>
        public int WindDeg { get; set; }

        /// <summary>Человекочитаемое направление ветра — "С", "ЮВ", "З" и т.д.</summary>
        public string WindDirection => DegToCompass(WindDeg);

        /// <summary>Порывы ветра, м/с (если есть в ответе).</summary>
        public double? WindGust { get; set; }

        /// <summary>Облачность, %.</summary>
        public int Cloudiness { get; set; }

        /// <summary>Видимость, метры (обычно до 10000).</summary>
        public int? Visibility { get; set; }

        /// <summary>Время восхода (Unix epoch, UTC).</summary>
        public long? SunriseUnix { get; set; }

        /// <summary>Время заката (Unix epoch, UTC).</summary>
        public long? SunsetUnix { get; set; }

        private static string DegToCompass(int deg)
        {
            // 16 румбов — даёт «С», «СВ», «В», «ЮВ» и т.д.
            string[] compass = {
                "С", "ССВ", "СВ", "ВСВ",
                "В", "ВЮВ", "ЮВ", "ЮЮВ",
                "Ю", "ЮЮЗ", "ЮЗ", "ЗЮЗ",
                "З", "ЗСЗ", "СЗ", "ССЗ"
            };
            if (deg < 0 || deg > 360) return "—";
            int idx = (int)System.Math.Round(((deg % 360) / 22.5)) % 16;
            return compass[idx];
        }
    }
}
