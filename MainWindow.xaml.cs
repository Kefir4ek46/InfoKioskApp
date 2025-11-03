using InfoKioskApp.Services;
using InfoKioskApp.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace InfoKioskApp
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _lessonTimer;
        private List<LessonTime> _bellSchedule;

        public MainWindow()
        {
            InitializeComponent();
            StartClock();
            LoadBellSchedule();
            StartLessonTimer();
            ApplyInterfaceSettings();
            _ = UpdateWeatherAsync();
        }

        #region === Время и дата ===
        private void StartClock()
        {
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) =>
            {
                TimeText.Text = DateTime.Now.ToString("HH:mm");
                DateText.Text = DateTime.Now.ToString("dddd, dd MMMM yyyy", new CultureInfo("ru-RU"));
            };
            _clockTimer.Start();
        }
        #endregion

        #region === Погода ===
        private async Task<(double lat, double lon)?> GetCoordinatesAsync(string city)
        {
            try
            {
                string q = Uri.EscapeDataString(city);
                string url = $"https://nominatim.openstreetmap.org/search?format=json&q={q}";

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "InfoKioskApp");
                    string json = await client.GetStringAsync(url);
                    dynamic arr = JsonConvert.DeserializeObject(json);
                    if (arr == null || arr.Count == 0) return null;

                    string latStr = (string)arr[0].lat;
                    string lonStr = (string)arr[0].lon;

                    double lat = double.Parse(latStr, CultureInfo.InvariantCulture);
                    double lon = double.Parse(lonStr, CultureInfo.InvariantCulture);
                    return (lat, lon);
                }
            }
            catch
            {
                return null;
            }
        }

        private string GetWeatherIcon(int code)
        {
            if (code == 0) return "☀️";
            if (code == 1 || code == 2) return "🌤️";
            if (code == 3) return "☁️";
            if (code >= 45 && code <= 48) return "🌫️";
            if (code >= 51 && code <= 57) return "🌦️";
            if (code >= 61 && code <= 67) return "🌧️";
            if (code >= 71 && code <= 77) return "❄️";
            if (code >= 80 && code <= 82) return "🌧️";
            if (code >= 95 && code <= 99) return "⛈️";
            return "🌍";
        }

        private async Task UpdateWeatherAsync()
        {
            try
            {
                var config = ConfigService.LoadConfig();
                string city = config.City ?? "Warsaw";

                double lat, lon;

                if (config.CachedLatitude != 0 && config.CachedLongitude != 0)
                {
                    lat = config.CachedLatitude;
                    lon = config.CachedLongitude;
                }
                else
                {
                    var coords = await GetCoordinatesAsync(city);
                    if (coords == null)
                    {
                        WeatherTemp.Text = "--°C";
                        WeatherCity.Text = "город не найден";
                        return;
                    }

                    lat = coords.Value.lat;
                    lon = coords.Value.lon;

                    config.CachedLatitude = lat;
                    config.CachedLongitude = lon;
                    ConfigService.SaveConfig(config);
                }

                string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";
                using (HttpClient client = new HttpClient())
                {
                    string json = await client.GetStringAsync(url);
                    dynamic data = JsonConvert.DeserializeObject(json);

                    double temp = data.current_weather.temperature;
                    int weatherCode = data.current_weather.weathercode;

                    WeatherTemp.Text = $"{temp:F0}°C";
                    WeatherCity.Text = city;
                    WeatherIcon.Text = GetWeatherIcon(weatherCode);
                }
            }
            catch (Exception ex)
            {
                WeatherTemp.Text = "--°C";
                WeatherCity.Text = "нет данных";
                Console.WriteLine($"Ошибка погоды: {ex.Message}");
            }
        }
        #endregion

        #region === Звонки ===
        private void LoadBellSchedule()
        {
            try
            {
                string path = "data/BellSchedule.json";
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _bellSchedule = JsonConvert.DeserializeObject<List<LessonTime>>(json);
                }
                else
                {
                    _bellSchedule = new List<LessonTime>();
                }
            }
            catch
            {
                _bellSchedule = new List<LessonTime>();
            }
        }

        private void StartLessonTimer()
        {
            _lessonTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _lessonTimer.Tick += (s, e) => UpdateLessonStatus();
            _lessonTimer.Start();
            UpdateLessonStatus();
        }

        private void UpdateLessonStatus()
        {
            if (_bellSchedule == null || _bellSchedule.Count == 0)
            {
                LessonStatusText.Text = "Нет данных о звонках";
                LessonTimeInfo.Text = "";
                return;
            }

            DateTime now = DateTime.Now;

            foreach (var lesson in _bellSchedule)
            {
                var start = DateTime.Today.Add(TimeSpan.Parse(lesson.Start));
                var end = DateTime.Today.Add(TimeSpan.Parse(lesson.End));

                if (now >= start && now <= end)
                {
                    var elapsed = now - start;
                    var remaining = end - now;

                    LessonStatusText.Text = $"Идёт {lesson.Number}-й урок";
                    LessonTimeInfo.Text = $"Прошло: {elapsed.Minutes} мин, осталось: {remaining.Minutes} мин";
                    return;
                }
            }

            var first = _bellSchedule[0];
            var last = _bellSchedule[_bellSchedule.Count - 1];
            var firstStart = DateTime.Today.Add(TimeSpan.Parse(first.Start));
            var lastEnd = DateTime.Today.Add(TimeSpan.Parse(last.End));

            if (now < firstStart)
            {
                var until = firstStart - now;
                LessonStatusText.Text = "До начала уроков";
                LessonTimeInfo.Text = $"Осталось {until.Minutes} мин";
            }
            else if (now > lastEnd)
            {
                LessonStatusText.Text = "Уроки окончены";
                LessonTimeInfo.Text = "";
            }
            else
            {
                LessonStatusText.Text = "Перемена";
                LessonTimeInfo.Text = "";
            }
        }
        #endregion

        #region === Навигация ===
        private void Calendar_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new CalendarView();
        }

        private void Documents_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new DocumentsView();
        }

        private void Media_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new MediaView();
        }

        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new ScheduleView();
        }

        private void Admin_Click(object sender, RoutedEventArgs e)
        {
            var pinWindow = new PinWindow();
            if (pinWindow.ShowDialog() == true && pinWindow.IsAuthorized)
            {
                var adminMenu = new AdminMenu();
                adminMenu.ShowDialog();
            }
        }
        #endregion

        #region === Настройки интерфейса ===
        private void ApplyInterfaceSettings()
        {
            var config = ConfigService.LoadConfig();

            if (config.InterfaceSettings != null)
            {
                var bg = (SolidColorBrush)new BrushConverter().ConvertFromString(config.InterfaceSettings.BackgroundColor ?? "#1E1E1E");
                var btnBg = (SolidColorBrush)new BrushConverter().ConvertFromString(config.InterfaceSettings.ButtonBackground ?? "#3A3A3A");
                var btnFg = (SolidColorBrush)new BrushConverter().ConvertFromString(config.InterfaceSettings.ButtonForeground ?? "White");

                Resources["AppBackgroundBrush"] = bg;
                Resources["PanelBackgroundBrush"] = bg;
                Resources["ButtonBackgroundBrush"] = btnBg;
                Resources["ButtonForegroundBrush"] = btnFg;
                Resources["TextForegroundBrush"] = btnFg;
            }
        }
        #endregion

        public class LessonTime
        {
            public int Number { get; set; }
            public string Start { get; set; }
            public string End { get; set; }
        }
    }
}
