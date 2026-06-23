using InfoKioskApp.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace InfoKioskApp.Services
{
    public static class WeatherService
    {
        /// <summary>
        /// Получает погоду по городу или координатам.
        ///
        /// Логика:
        ///   1. Читаем конфиг — оттуда берём OpenWeatherApiKey, City,
        ///      CachedLatitude, CachedLongitude.
        ///   2. Если в конфиге есть координаты (lat != 0 И lon != 0) —
        ///      запрашиваем погоду по координатам (самый надёжный способ).
        ///   3. Если координат нет — геокодируем город (через OpenWeather
        ///      Geocoding API), получаем lat/lon, сохраняем их в конфиг
        ///      (CachedLatitude/CachedLongitude), и запрашиваем погоду.
        ///   4. Если город не найден — возвращаем null.
        ///
        /// API key берётся из config.OpenWeatherApiKey, а не хардкодится.
        /// </summary>
        public static async Task<WeatherData> GetWeatherAsync(string city)
        {
            try
            {
                var cfg = ConfigService.LoadConfig();
                string apiKey = string.IsNullOrWhiteSpace(cfg.OpenWeatherApiKey)
                    ? "4fca50ecc7eb8fa8d8ae1021853e0e7c"  // fallback на дефолтный
                    : cfg.OpenWeatherApiKey;

                if (string.IsNullOrWhiteSpace(city))
                    city = cfg.City ?? "Moscow";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                double lat = cfg.CachedLatitude;
                double lon = cfg.CachedLongitude;

                // Если координат нет — геокодируем город.
                if (lat == 0 || lon == 0)
                {
                    Console.WriteLine($"[Weather] Geocoding city: {city}");
                    var (geoLat, geoLon, geoName) = await GeocodeCityAsync(client, apiKey, city);
                    if (geoLat == 0 && geoLon == 0)
                    {
                        Console.WriteLine($"[Weather] City not found: {city}");
                        return null;
                    }
                    lat = geoLat;
                    lon = geoLon;

                    // Сохраняем координаты в конфиг, чтобы не геокодировать каждый раз.
                    try
                    {
                        cfg.CachedLatitude = lat;
                        cfg.CachedLongitude = lon;
                        ConfigService.SaveConfig(cfg);
                        Console.WriteLine($"[Weather] Cached coordinates: {lat}, {lon}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Weather] Failed to cache coords: {ex.Message}");
                    }
                }

                // Запрашиваем погоду по координатам.
                Console.WriteLine($"[Weather] Fetching weather for {lat},{lon}");
                string url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={apiKey}&units=metric&lang=ru";
                string json = await client.GetStringAsync(url);

                var data = JObject.Parse(json);

                var w = new WeatherData
                {
                    City = (string)data["name"] ?? city,
                    Temperature = (double)data["main"]["temp"],
                    FeelsLike = (double)data["main"]["feels_like"],
                    Description = (string)data["weather"][0]["description"],
                    IconCode = (string)data["weather"][0]["icon"],
                    Humidity = (int)data["main"]["humidity"],
                    PressureHpa = (int)data["main"]["pressure"],
                    WindSpeed = (double)data["wind"]["speed"],
                    WindDeg = (int)data["wind"]["deg"],
                    Cloudiness = (int)(data["clouds"]?["all"] ?? 0),
                };

                if (data["wind"]?["gust"] != null)
                    w.WindGust = (double)data["wind"]["gust"];
                if (data["visibility"] != null)
                    w.Visibility = (int)data["visibility"];
                if (data["sys"]?["sunrise"] != null)
                    w.SunriseUnix = (long)data["sys"]["sunrise"];
                if (data["sys"]?["sunset"] != null)
                    w.SunsetUnix = (long)data["sys"]["sunset"];

                Console.WriteLine($"[Weather] OK: {w.City} {w.Temperature}°C {w.Description}");
                return w;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherService] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Геокодирует город через OpenWeather Geocoding API.
        /// Возвращает (lat, lon, name) или (0, 0, null) если не найдено.
        /// </summary>
        public static async Task<(double lat, double lon, string name)> GeocodeCityAsync(HttpClient client, string apiKey, string city)
        {
            try
            {
                string url = $"https://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(city)}&limit=1&appid={apiKey}";
                string json = await client.GetStringAsync(url);
                var arr = JArray.Parse(json);
                if (arr.Count == 0) return (0, 0, null);
                var first = arr[0];
                double lat = (double)first["lat"];
                double lon = (double)first["lon"];
                string name = (string)first["name"] ?? city;
                return (lat, lon, name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Weather] Geocode failed: {ex.Message}");
                return (0, 0, null);
            }
        }
    }
}
