using InfoKioskApp.Models;
using InfoKioskApp.Services;
using InfoKioskApp.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static InfoKioskApp.AppConfig;


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
            StartWeatherTimer();
            

            // ✅ Добавляем пользовательские разделы из конфига
            AddCustomSections();
            var config = ConfigService.LoadConfig();
            if (config.RemoteAutoStart)
            {
                try
                {
                    RemoteServerService.Start(config.RemotePort);
                    Console.WriteLine($"🌐 Сервер запущен автоматически на порту {config.RemotePort}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка автозапуска сервера: {ex.Message}");
                }
            }

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
        private string GetWeatherEmoji(int code)
        {
            if (code >= 200 && code < 300) return "⛈️"; // гроза
            if (code >= 300 && code < 400) return "🌦️"; // морось
            if (code >= 500 && code < 600) return "🌧️"; // дождь
            if (code >= 600 && code < 700) return "❄️"; // снег
            if (code >= 700 && code < 800) return "🌫️"; // туман
            if (code == 800) return "☀️";               // ясно
            if (code > 800) return "⛅";                 // облачно
            return "🌍";
        }

        private async Task UpdateWeatherAsync()
        {
            try
            {
                var config = ConfigService.LoadConfig();
                double lat = config.CachedLatitude;
                double lon = config.CachedLongitude;
                string city = config.City ?? "Местоположение не указано";

                if (lat == 0 || lon == 0)
                {
                    WeatherTemp.Text = "--°C";
                    WeatherCity.Text = "Не заданы координаты";
                    WeatherExtra.Text = "Введите широту и долготу в настройках";
                    WeatherTomorrow.Text = "";
                    return;
                }

                string apiKey = "4fca50ecc7eb8fa8d8ae1021853e0e7c"; // OpenWeather API ключ
                string url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&units=metric&lang=ru&appid={apiKey}";

                using (HttpClient client = new HttpClient())
                {
                    string json = await client.GetStringAsync(url);
                    dynamic data = JsonConvert.DeserializeObject(json);

                    double temp = data.main.temp;
                    double wind = data.wind.speed;
                    int humidity = data.main.humidity;
                    int pressure = data.main.pressure;
                    int code = data.weather[0].id;
                    string description = data.weather[0].description;

                    WeatherIcon.Text = GetWeatherEmoji(code);
                    WeatherTemp.Text = $"{temp:F0}°C";
                    WeatherCity.Text = city;
                    WeatherExtra.Text = $"{description}, ветер {wind:F1} м/с, влажность {humidity}%, давление {pressure} гПа";
                    WeatherTomorrow.Text = "";
                }
            }
            catch (Exception ex)
            {
                WeatherTemp.Text = "--°C";
                WeatherCity.Text = "Ошибка загрузки";
                WeatherExtra.Text = "";
                WeatherTomorrow.Text = "";
                Console.WriteLine($"Ошибка погоды: {ex.Message}");
            }
        }

        private DispatcherTimer _weatherTimer;
        private void StartWeatherTimer()
        {
            _weatherTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(15)
            };
            _weatherTimer.Tick += async (s, e) => await UpdateWeatherAsync();
            _weatherTimer.Start();
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

            for (int i = 0; i < _bellSchedule.Count; i++)
            {
                var lesson = _bellSchedule[i];
                var start = DateTime.Today.Add(TimeSpan.Parse(lesson.Start));
                var end = DateTime.Today.Add(TimeSpan.Parse(lesson.End));

                // 🟢 Идёт урок
                if (now >= start && now <= end)
                {
                    var elapsed = now - start;
                    var remaining = end - now;

                    LessonStatusText.Text = $"Идёт {lesson.Number}-й урок";
                    LessonTimeInfo.Text = $"Прошло: {Math.Floor(elapsed.TotalMinutes)} мин, осталось: {Math.Ceiling(remaining.TotalMinutes)} мин";
                    return;
                }

                // 🟡 Перемена между уроками
                if (i < _bellSchedule.Count - 1)
                {
                    var next = _bellSchedule[i + 1];
                    var nextStart = DateTime.Today.Add(TimeSpan.Parse(next.Start));

                    if (now > end && now < nextStart)
                    {
                        var untilNext = nextStart - now;
                        LessonStatusText.Text = $"Перемена между {lesson.Number}-м и {next.Number}-м уроками";
                        LessonTimeInfo.Text = $"До звонка осталось {Math.Ceiling(untilNext.TotalMinutes)} мин";
                        return;
                    }
                }
            }

            // 💤 До начала уроков
            var first = _bellSchedule.First();
            var firstStart = DateTime.Today.Add(TimeSpan.Parse(first.Start));

            if (now < firstStart)
            {
                var until = firstStart - now;
                LessonStatusText.Text = "До начала уроков";
                LessonTimeInfo.Text = $"Осталось {Math.Ceiling(until.TotalMinutes)} мин";
                return;
            }

            // 🏁 После уроков
            var last = _bellSchedule.Last();
            var lastEnd = DateTime.Today.Add(TimeSpan.Parse(last.End));

            if (now > lastEnd)
            {
                LessonStatusText.Text = "Уроки окончены";
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
        private void SchoolSite_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new SchoolWebsiteView();
        }
        


        private void Admin_Click(object sender, RoutedEventArgs e)
        {
            var pinWindow = new PinWindow();
            if (pinWindow.ShowDialog() == true && pinWindow.IsAuthorized)
            {
                var adminMenu = new AdminMenu();
                adminMenu.ShowDialog();

                // 🔄 После выхода из админ-панели обновляем меню (вдруг добавились новые разделы)
                AddCustomSections(true);
            }
        }
        #endregion

        #region === Пользовательские разделы ===
        private void AddCustomSections(bool refresh = false)
        {
            var config = ConfigService.LoadConfig();
            if (config.CustomSections == null || config.CustomSections.Count == 0)
                return;

            var leftMenu = FindName("LeftMenuPanel") as StackPanel;
            if (leftMenu == null) return;

            // Удаляем старые кнопки пользовательских разделов
            if (refresh)
            {
                var toRemove = leftMenu.Children.OfType<Button>().Where(b => b.Tag is CustomSection).ToList();
                foreach (var b in toRemove) leftMenu.Children.Remove(b);
            }

            foreach (var section in config.CustomSections)
            {
                var button = new Button
                {
                    Content = $"📂 {section.Name}",
                    Style = (Style)FindResource("ModernMenuButton"),
                    Tag = section
                };

                button.Click += (s, e) =>
                {
                    ContentArea.Content = new CustomContentView(section.FolderPath);

                };

                leftMenu.Children.Add(button);
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
