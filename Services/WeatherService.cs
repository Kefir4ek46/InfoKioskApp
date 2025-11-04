using InfoKioskApp.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace InfoKioskApp.Services
{
    public static class WeatherService
    {
        private const string ApiKey = "4fca50ecc7eb8fa8d8ae1021853e0e7c"; // ⚠️ сюда вставь ключ от openweathermap.org

        public static async Task<WeatherData> GetWeatherAsync(string city)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={ApiKey}&units=metric&lang=ru";
                    string json = await client.GetStringAsync(url);

                    var data = JObject.Parse(json);
                    return new WeatherData
                    {
                        City = (string)data["name"],
                        Temperature = (double)data["main"]["temp"],
                        Description = (string)data["weather"][0]["description"],
                        IconCode = (string)data["weather"][0]["icon"]
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения погоды: {ex.Message}");
                return null;
            }
        }
    }
}
