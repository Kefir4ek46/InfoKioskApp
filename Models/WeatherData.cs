namespace InfoKioskApp.Models
{
    public class WeatherData
    {
        public string City { get; set; }
        public double Temperature { get; set; }
        public string Description { get; set; }
        public string IconCode { get; set; }
        public string IconUrl => $"https://openweathermap.org/img/wn/{IconCode}@2x.png";
    }
}
