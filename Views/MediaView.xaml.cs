using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace InfoKioskApp.Views
{
    public partial class MediaView : UserControl
    {
        private string _mediaPath = "data/media";
        private string[] _mediaFiles;
        private int _currentIndex = 0;
        private DispatcherTimer _mediaTimer;

        public MediaView()
        {
            InitializeComponent();
            LoadMedia();
        }

        private void LoadMedia()
        {
            if (!Directory.Exists(_mediaPath))
                Directory.CreateDirectory(_mediaPath);

            _mediaFiles = Directory.GetFiles(_mediaPath, "*.*")
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (_mediaFiles.Length > 0)
            {
                ShowImage();
                StartSlideshow();
            }
            else
            {
                MediaImage.Source = null;
            }
        }

        private void ShowImage()
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_mediaFiles[_currentIndex]);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                MediaImage.Source = bitmap;
            }
            catch
            {
                MediaImage.Source = null;
            }
        }

        private void StartSlideshow()
        {
            _mediaTimer = new DispatcherTimer();
            _mediaTimer.Interval = TimeSpan.FromSeconds(8);
            _mediaTimer.Tick += (s, e) => NextImage();
            _mediaTimer.Start();
        }

        private void NextImage()
        {
            if (_mediaFiles == null || _mediaFiles.Length == 0) return;
            _currentIndex = (_currentIndex + 1) % _mediaFiles.Length;
            ShowImage();
        }

        private void PrevImage()
        {
            if (_mediaFiles == null || _mediaFiles.Length == 0) return;
            _currentIndex = (_currentIndex - 1 + _mediaFiles.Length) % _mediaFiles.Length;
            ShowImage();
        }

        private void Next_Click(object sender, RoutedEventArgs e) => NextImage();
        private void Prev_Click(object sender, RoutedEventArgs e) => PrevImage();
    }
}

