using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InfoKioskApp.Services;
using QRCoder;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class RemoteSettingsView : UserControl
    {
        private bool _serverRunning;
        private string _currentUrl;

        public RemoteSettingsView()
        {
            InitializeComponent();
            LoadSettings();
            UpdateNetworkInfo();

            // ✅ Подписываемся на изменение статуса сервера
            RemoteServerService.ServerStatusChanged += OnServerStatusChanged;
        }

        private void LoadSettings()
        {
            var config = ConfigService.LoadConfig();
            PortBox.Text = config.RemotePort > 0 ? config.RemotePort.ToString() : "8080";
            AutoStartCheck.IsChecked = config.RemoteAutoStart;

            // Если сервер уже работает — обновляем статус
            if (RemoteServerService.IsRunning)
            {
                _serverRunning = true;
                UpdateServerStatus(true);
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortBox.Text, out int port) || port < 1024 || port > 65535)
            {
                MessageBox.Show("Введите корректный порт (1024–65535).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var config = ConfigService.LoadConfig();
            config.RemotePort = port;
            config.RemoteAutoStart = AutoStartCheck.IsChecked == true;
            ConfigService.SaveConfig(config);

            MessageBox.Show("Настройки удалённого доступа сохранены ✅", "Сохранено", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateNetworkInfo()
        {
            string ip = GetLocalIp();
            IpText.Text = ip ?? "Не найден";

            if (!_serverRunning)
            {
                ServerStatus.Text = "Сервер не запущен";
                ServerStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
                QrPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void StartServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(PortBox.Text, out int port))
                    port = 8080;

                // 🟢 Запускаем сервер
                RemoteServerService.Start(port);
                _serverRunning = true;

                // Обновляем интерфейс
                string ip = GetLocalIp();
                _currentUrl = $"http://{ip}:{port}/";
                UpdateServerStatus(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска сервера: {ex.Message}");
            }
        }

        private void StopServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RemoteServerService.Stop();
                _serverRunning = false;
                UpdateServerStatus(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки сервера: {ex.Message}");
            }
        }

        private void UpdateServerStatus(bool running)
        {
            if (running)
            {
                string ip = GetLocalIp();
                _currentUrl = $"http://{ip}:{PortBox.Text}/";

                ServerStatus.Text = "Сервер запущен ✅";
                ServerStatus.Foreground = new SolidColorBrush(Colors.LimeGreen);

                QrPanel.Visibility = Visibility.Visible;
                ConnectUrl.Text = _currentUrl;
                GenerateQr(_currentUrl);
            }
            else
            {
                ServerStatus.Text = "Сервер остановлен ⏹";
                ServerStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
                QrPanel.Visibility = Visibility.Collapsed;
            }
        }

        // 🟢 Обновляем UI при изменении статуса сервера
        private void OnServerStatusChanged(bool running)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _serverRunning = running;
                UpdateServerStatus(running);
            });
        }

        private void GenerateQr(string text)
        {
            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrData);
            byte[] qrBytes = qrCode.GetGraphic(20);

            using (var ms = new MemoryStream(qrBytes))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                QrImage.Source = img;
            }
        }

        private string GetLocalIp()
        {
            var ip = NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .FirstOrDefault();

            return ip ?? "localhost";
        }
    }
}
