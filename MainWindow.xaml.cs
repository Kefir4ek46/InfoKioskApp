using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using InfoKioskApp.Views;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

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
            _ = UpdateWeatherAsync(); // Асинхронный вызов без ожидания
        }

        #region === Время и дата ===
        private void StartClock()
        {
            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) =>
            {
                TimeText.Text = DateTime.Now.ToString("HH:mm");
                DateText.Text = DateTime.Now.ToString("dddd, dd MMMM yyyy");
            };
            _clockTimer.Start();
        }
        #endregion

        #region === Погода ===
        private async Task UpdateWeatherAsync()
        {
            try
            {
                string apiKey = "YOUR_API_KEY"; // 🔑 вставь сюда ключ OpenWeatherMap
                string city = "Warsaw";
                string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&units=metric&appid={apiKey}&lang=ru";

                using (HttpClient client = new HttpClient())
                {
                    string json = await client.GetStringAsync(url);

                    JObject data = JObject.Parse(json);
                    double temp = data["main"]["temp"].Value<double>();
                    string icon = data["weather"][0]["icon"].Value<string>();

                    WeatherTemp.Text = $"{temp:F0}°C";
                    WeatherCity.Text = city;
                    WeatherIcon.Text = GetWeatherIcon(icon);
                }
            }
            catch
            {
                WeatherTemp.Text = "--°C";
                WeatherCity.Text = "нет данных";
            }
        }

        private string GetWeatherIcon(string code)
        {
            if (code.StartsWith("01")) return "☀️";
            if (code.StartsWith("02")) return "🌤️";
            if (code.StartsWith("03") || code.StartsWith("04")) return "☁️";
            if (code.StartsWith("09")) return "🌧️";
            if (code.StartsWith("10")) return "🌦️";
            if (code.StartsWith("11")) return "⛈️";
            if (code.StartsWith("13")) return "❄️";
            if (code.StartsWith("50")) return "🌫️";
            return "🌍";
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
            _lessonTimer = new DispatcherTimer();
            _lessonTimer.Interval = TimeSpan.FromSeconds(30);
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
    }

    public class LessonTime
    {
        public int Number { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
    }
}
