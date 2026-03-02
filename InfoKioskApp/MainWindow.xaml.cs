using InfoKioskApp.Models;
using InfoKioskApp.Services;
using InfoKioskApp.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static InfoKioskApp.Models.AppConfig;


namespace InfoKioskApp
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _lessonTimer;
        private List<LessonTime> _bellSchedule;

        private SchoolWebsiteView? _schoolWebsiteView;
        private DocumentsView? _documentsView;
        private MediaView? _mediaView;
        private ScheduleView? _scheduleView;
        private CalendarView? _calendarView;
        private NewsView? _newsView;


        private double _tickerX;
        private double _tickerSpeed = 1.5;
        private DateTime _lastTickerFrameTime = DateTime.UtcNow;
        private List<string> _tickerItems = [];
        private int _tickerItemIndex;

        private FileSystemWatcher? _configWatcher;

        private DispatcherTimer _idleTimer;
        private DispatcherTimer _idleSlideTimer;
        private List<string> _idleImages = new();
        private int _idleImageIndex;

        private DispatcherTimer? _sleepScheduleTimer;
        private string _lastSleepTriggerKey = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            StartClock();
            LoadBellSchedule();
            StartLessonTimer();
            ApplyRuntimeSettings();
            _ = UpdateWeatherAsync();
            StartWeatherTimer();
            BindActivityEvents();
            SetupConfigWatcher();

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


        private void ApplyRuntimeSettings()
        {
            ApplyInterfaceSettings();
            InitializeTicker();
            InitializeIdleScreen();
            InitializeSleepSchedule();
        }

        private void SetupConfigWatcher()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "config.json");
                string configDir = Path.GetDirectoryName(configPath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                string configFile = Path.GetFileName(configPath);
                Directory.CreateDirectory(configDir);

                _configWatcher = new FileSystemWatcher(configDir, configFile)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false
                };

                void refresh(object? s, FileSystemEventArgs e)
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(150);
                        ApplyRuntimeSettings();
                    });
                }

                _configWatcher.Changed += refresh;
                _configWatcher.Created += refresh;
                _configWatcher.Renamed += (s, e) => refresh(s, e);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config watcher error: {ex.Message}");
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
        private static string GetWeatherEmoji(int code)
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

                HttpClient httpClient = new();
                using HttpClient client = httpClient;
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
                    _bellSchedule = [];
                }
            }
            catch
            {
                _bellSchedule = [];
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
            _calendarView ??= new CalendarView();
            ContentArea.Content = _calendarView;
        }
        private void Canteen_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new CanteenMenuView();
        }



        private void Documents_Click(object sender, RoutedEventArgs e)
        {
            _documentsView ??= new DocumentsView();
            ContentArea.Content = _documentsView;
        }


        private void Media_Click(object sender, RoutedEventArgs e)
        {
            _mediaView ??= new MediaView();
            ContentArea.Content = _mediaView;
        }


        private void News_Click(object sender, RoutedEventArgs e)
        {
            _newsView ??= new NewsView();
            ContentArea.Content = _newsView;
        }

        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            _scheduleView ??= new ScheduleView();
            ContentArea.Content = _scheduleView;
        }

        private void SchoolSite_Click(object sender, RoutedEventArgs e)
        {
            _schoolWebsiteView ??= new SchoolWebsiteView();
            ContentArea.Content = _schoolWebsiteView;

            _schoolWebsiteView.OpenSite(); // 🔥 теперь прогрев работает
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
                ApplyRuntimeSettings();
            }
        }
        #endregion

        #region === Пользовательские разделы ===
        private void AddCustomSections(bool refresh = false)
        {
            var config = ConfigService.LoadConfig();
            if (config.CustomSections == null || config.CustomSections.Count == 0)
                return;

            if (FindName("LeftMenuPanel") is not StackPanel leftMenu) return;

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
            var ui = config.InterfaceSettings ?? new InterfaceSettings();

            string theme = (ui.Theme ?? "dark").ToLowerInvariant();
            if (theme == "light")
            {
                ui.BackgroundColor ??= "#F5F5F5";
                ui.ButtonBackground ??= "#E0E0E0";
                ui.ButtonForeground ??= "#222222";
                ui.NavigationButtonBackground ??= "#E0E0E0";
                ui.NavigationButtonForeground ??= "#222222";
            }
            else
            {
                ui.BackgroundColor ??= "#1E1E1E";
                ui.ButtonBackground ??= "#3A3A3A";
                ui.ButtonForeground ??= "White";
                ui.NavigationButtonBackground ??= "#3A3A3A";
                ui.NavigationButtonForeground ??= "White";
            }

            var bg = (SolidColorBrush)new BrushConverter().ConvertFromString(ui.BackgroundColor);
            var btnBg = (SolidColorBrush)new BrushConverter().ConvertFromString(ui.ButtonBackground);
            var btnFg = (SolidColorBrush)new BrushConverter().ConvertFromString(ui.ButtonForeground);
            var navBg = (SolidColorBrush)new BrushConverter().ConvertFromString(ui.NavigationButtonBackground);
            var navFg = (SolidColorBrush)new BrushConverter().ConvertFromString(ui.NavigationButtonForeground);

            Resources["AppBackgroundBrush"] = bg;
            Resources["PanelBackgroundBrush"] = bg;
            Resources["ButtonBackgroundBrush"] = btnBg;
            Resources["ButtonForegroundBrush"] = btnFg;
            Resources["TextForegroundBrush"] = btnFg;

            var appResources = Application.Current?.Resources;
            if (appResources != null)
            {
                appResources["AppFontFamily"] = new FontFamily(ui.FontFamily ?? "Segoe UI");
                appResources["AppFontSize"] = ui.FontSize <= 0 ? 14 : ui.FontSize;
            }

            if (FindName("LeftMenuPanel") is StackPanel leftMenu)
            {
                foreach (var button in leftMenu.Children.OfType<Button>())
                {
                    button.Background = navBg;
                    button.Foreground = navFg;
                    button.FontFamily = new FontFamily(ui.FontFamily ?? "Segoe UI");
                    button.FontSize = ui.NavigationButtonFontSize > 0 ? ui.NavigationButtonFontSize : 15;
                }
            }

            TickerTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(config.Ticker?.Foreground ?? "#FFFFFF"));
        }

        private void InitializeTicker()
        {
            var config = ConfigService.LoadConfig();
            var ticker = config.Ticker ?? new AppConfig.TickerSettings();

            _tickerItems = (ticker.Items ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
            if (_tickerItems.Count == 0 && !string.IsNullOrWhiteSpace(ticker.Text))
                _tickerItems.Add(ticker.Text.Trim());

            _tickerItemIndex = 0;
            TickerTextBlock.Text = _tickerItems.FirstOrDefault() ?? string.Empty;
            TickerTextBlock.FontSize = ticker.FontSize > 0 ? ticker.FontSize : 20;
            TickerTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ticker.Foreground ?? "#FFFFFF"));
            _tickerSpeed = Math.Max(20, (ticker.Speed <= 0 ? 1.5 : ticker.Speed) * 60);

            CompositionTarget.Rendering -= OnTickerRendering;

            if (!ticker.Enabled || _tickerItems.Count == 0 || string.IsNullOrWhiteSpace(TickerTextBlock.Text))
            {
                TickerCanvas.Visibility = Visibility.Collapsed;
                return;
            }

            TickerCanvas.Visibility = Visibility.Visible;
            TickerCanvas.UpdateLayout();
            TickerTextBlock.UpdateLayout();
            _tickerX = TickerCanvas.ActualWidth;
            Canvas.SetLeft(TickerTextBlock, _tickerX);
            Canvas.SetTop(TickerTextBlock, 6);
            _lastTickerFrameTime = DateTime.UtcNow;
            CompositionTarget.Rendering += OnTickerRendering;
        }

        private void OnTickerRendering(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            var dt = (now - _lastTickerFrameTime).TotalSeconds;
            _lastTickerFrameTime = now;
            if (dt <= 0 || dt > 0.5)
                return;

            _tickerX -= _tickerSpeed * dt;
            if (_tickerX < -TickerTextBlock.ActualWidth)
            {
                _tickerItemIndex = (_tickerItemIndex + 1) % _tickerItems.Count;
                TickerTextBlock.Text = _tickerItems[_tickerItemIndex];
                TickerTextBlock.UpdateLayout();
                _tickerX = TickerCanvas.ActualWidth;
            }

            Canvas.SetLeft(TickerTextBlock, _tickerX);
        }

        private void BindActivityEvents()
        {
            PreviewMouseDown += (_, __) => OnUserActivity();
            PreviewMouseMove += (_, __) => OnUserActivity();
            PreviewKeyDown += (_, __) => OnUserActivity();
            TouchDown += (_, __) => OnUserActivity();
        }

        private void InitializeIdleScreen()
        {
            var cfg = ConfigService.LoadConfig().IdleScreen ?? new AppConfig.IdleScreenSettings();
            _idleTimer ??= new DispatcherTimer();
            _idleTimer.Stop();

            if (!cfg.Enabled)
            {
                IdleOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            _idleTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, cfg.TimeoutSeconds));
            _idleTimer.Tick -= IdleTimerTick;
            _idleTimer.Tick += IdleTimerTick;
            _idleTimer.Start();

            LoadIdleImages();

            _idleSlideTimer ??= new DispatcherTimer();
            _idleSlideTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, cfg.SlideDurationSeconds));
            _idleSlideTimer.Tick -= IdleSlideTimerTick;
            _idleSlideTimer.Tick += IdleSlideTimerTick;

            IdleOverlay.Visibility = Visibility.Collapsed;
        }

        private void LoadIdleImages()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            string logoFolder = Path.Combine(root, "schoolLogo");
            string photosFolder = Path.Combine(root, "schoolPhotos");
            Directory.CreateDirectory(logoFolder);
            Directory.CreateDirectory(photosFolder);

            var allowed = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
            _idleImages = new List<string>();

            var logo = Directory.GetFiles(logoFolder).FirstOrDefault(f => allowed.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (!string.IsNullOrEmpty(logo))
                _idleImages.Add(logo);

            _idleImages.AddRange(Directory.GetFiles(photosFolder).Where(f => allowed.Contains(Path.GetExtension(f).ToLowerInvariant())));
            _idleImageIndex = 0;
        }

        private void IdleTimerTick(object sender, EventArgs e)
        {
            ShowIdleOverlay();
        }

        private void IdleSlideTimerTick(object sender, EventArgs e)
        {
            if (_idleImages.Count == 0) return;
            _idleImageIndex = (_idleImageIndex + 1) % _idleImages.Count;
            ShowIdleImage(_idleImages[_idleImageIndex]);
        }

        private void ShowIdleOverlay()
        {
            LoadIdleImages();
            IdleOverlay.Visibility = Visibility.Visible;

            if (_idleImages.Count > 0)
                ShowIdleImage(_idleImages[0]);

            var cfg = ConfigService.LoadConfig().IdleScreen ?? new AppConfig.IdleScreenSettings();
            if (!cfg.ShowLogoOnly && _idleImages.Count > 1)
                _idleSlideTimer?.Start();
            else
                _idleSlideTimer?.Stop();
        }

        private void ShowIdleImage(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                IdleImage.Source = bitmap;
                var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700));
                IdleImage.BeginAnimation(OpacityProperty, anim);
            }
            catch { }
        }

        private void OnUserActivity()
        {
            if (IdleOverlay.Visibility == Visibility.Visible)
                IdleOverlay.Visibility = Visibility.Collapsed;

            _idleSlideTimer?.Stop();
            _idleTimer?.Stop();
            _idleTimer?.Start();
        }


        private void InitializeSleepSchedule()
        {
            _sleepScheduleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _sleepScheduleTimer.Tick -= SleepScheduleTick;

            var sleepAt = (ConfigService.LoadConfig().SleepAt ?? string.Empty).Trim();
            if (!TimeSpan.TryParseExact(sleepAt, "hh\\:mm", CultureInfo.InvariantCulture, out _))
            {
                _sleepScheduleTimer.Stop();
                return;
            }

            _sleepScheduleTimer.Tick += SleepScheduleTick;
            _sleepScheduleTimer.Start();
        }

        private void SleepScheduleTick(object? sender, EventArgs e)
        {
            var sleepAt = (ConfigService.LoadConfig().SleepAt ?? string.Empty).Trim();
            if (!TimeSpan.TryParseExact(sleepAt, "hh\\:mm", CultureInfo.InvariantCulture, out var t))
                return;

            var now = DateTime.Now;
            if (now.Hour != t.Hours || now.Minute != t.Minutes)
                return;

            string key = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            if (_lastSleepTriggerKey == key)
                return;

            _lastSleepTriggerKey = key;
            TrySleepDevice();
        }

        private static void TrySleepDevice()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (SetSuspendState(false, true, true))
                        return;

                    Process.Start(new ProcessStartInfo("shutdown", "/h") { UseShellExecute = false, CreateNoWindow = true });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sleep schedule error: {ex.Message}");
            }
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        #endregion


        public class LessonTime
        {
            public int Number { get; set; }
            public string Start { get; set; }
            public string End { get; set; }
        }
    }
}
