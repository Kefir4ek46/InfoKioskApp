using System;
using System.Windows;
using System.Windows.Threading;
using InfoKioskApp.Models;
using InfoKioskApp.Services;

namespace InfoKioskApp.Views
{
    public partial class PinWindow : Window
    {
        private readonly AppConfig _config;
        private DispatcherTimer _autoCloseTimer;

        public bool IsAuthorized { get; private set; } = false;

        public PinWindow()
        {
            InitializeComponent();

            _config = ConfigService.LoadConfig(); // ✅ твой конфиг
            StartAutoCloseTimer();

            // 🔄 если начали вводить PIN — продлеваем время
            PinInput.PasswordChanged += (_, _) => ResetTimer();
        }

        // ⏱ Таймер автозакрытия
        private void StartAutoCloseTimer()
        {
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20) // ⏱ 20 секунд
            };

            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                DialogResult = false;
                Close();
            };

            _autoCloseTimer.Start();
        }

        private void ResetTimer()
        {
            if (_autoCloseTimer == null)
                return;

            _autoCloseTimer.Stop();
            _autoCloseTimer.Start();
        }

        private void StopTimer()
        {
            _autoCloseTimer?.Stop();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();

            string input = PinInput.Password.Trim();

            if (input == _config.PinCode)
            {
                IsAuthorized = true;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(
                    "Неверный PIN-код!",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                PinInput.Clear();
                PinInput.Focus();

                // 🔄 даём ещё минуту после ошибки
                StartAutoCloseTimer();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
            DialogResult = false;
            Close();
        }
    }
}
