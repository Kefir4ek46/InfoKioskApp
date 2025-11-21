using InfoKioskApp.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace InfoKioskApp.Views
{
    public partial class MediaView : UserControl
    {
        private List<string> _mediaFiles;
        private int _currentIndex = 0;
        private DispatcherTimer _autoTimer;
        private bool _isPaused = false;

        public MediaView()
        {
            InitializeComponent();
            LoadMediaFiles();
            ShowMedia(_currentIndex);
            StartAutoCycle();
        }

        #region === Загрузка ===
        private void LoadMediaFiles()
        {
            var config = ConfigService.LoadConfig();
            string folder = config.MediaPath ?? "data/media";

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            _mediaFiles = Directory.GetFiles(folder)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        #endregion

        #region === Показ медиа ===
        private void ShowMedia(int index)
        {
            if (_mediaFiles == null || _mediaFiles.Count == 0)
            {
                ContentArea.Children.Clear();
                ContentArea.Children.Add(new TextBlock
                {
                    Text = "Нет медиафайлов для отображения",
                    Foreground = Brushes.Gray,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                return;
            }

            if (index < 0) _currentIndex = _mediaFiles.Count - 1;
            if (index >= _mediaFiles.Count) _currentIndex = 0;

            string path = _mediaFiles[_currentIndex];
            string ext = Path.GetExtension(path).ToLower();

            MediaTitle.Text = Path.GetFileName(path);

            ContentArea.Children.Clear();
            if (IsImage(ext)) ShowImage(path);
            else if (IsVideo(ext)) ShowVideo(path);
        }

        private void ShowImage(string path)
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(Path.GetFullPath(path))),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly, // ← ключевой параметр
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10)
            };

            // Картинка будет автоматически масштабироваться под размер ContentArea
            image.MaxWidth = ContentArea.ActualWidth - 20;
            image.MaxHeight = ContentArea.ActualHeight - 20;

            ContentArea.SizeChanged += (s, e) =>
            {
                image.MaxWidth = ContentArea.ActualWidth - 20;
                image.MaxHeight = ContentArea.ActualHeight - 20;
            };

            ContentArea.Children.Clear();
            ContentArea.Children.Add(image);
        }


        private void ShowVideo(string path)
        {
            var media = new MediaElement
            {
                Source = new Uri(Path.GetFullPath(path)),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                Volume = 0.4
            };

            media.MediaEnded += (s, e) => Next_Click(null, null);
            ContentArea.Children.Clear();
            ContentArea.Children.Add(media);
            media.Play();
        }
        #endregion

        #region === Навигация ===
        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            _currentIndex--;
            ShowMedia(_currentIndex);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            _currentIndex++;
            ShowMedia(_currentIndex);
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;

            if (_isPaused)
            {
                _autoTimer?.Stop();
                PlayPauseButton.Content = "▶ Автопрокрутка";
            }
            else
            {
                StartAutoCycle();
                PlayPauseButton.Content = "⏸ Остановить";
            }
        }
        #endregion

        #region === Автоматическая смена ===
        private void StartAutoCycle()
        {
            _autoTimer?.Stop();

            var config = ConfigService.LoadConfig();
            int seconds = config?.MediaAutoIntervalSeconds ?? 15;
            if (seconds < 5) seconds = 5;

            _autoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };
            _autoTimer.Tick += (s, e) => { if (!_isPaused) Next_Click(null, null); };
            _autoTimer.Start();

            PlayPauseButton.Content = _isPaused ? "▶ Автопрокрутка" : "⏸ Остановить";
        }
        #endregion

        private bool IsImage(string ext) =>
            ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".bmp";

        private bool IsVideo(string ext) =>
            ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".wmv";
    }
}
