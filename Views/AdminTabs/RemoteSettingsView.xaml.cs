using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using InfoKioskApp.Services;
using QRCoder;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class RemoteSettingsView : UserControl
    {
        private bool _serverRunning = false;
        private string _currentUrl;

        public RemoteSettingsView()
        {
            InitializeComponent();
            UpdateNetworkInfo();
        }

        private void UpdateNetworkInfo()
        {
            string ip = GetLocalIp();
            IpText.Text = ip ?? "Не найден";
            ServerStatus.Text = "Сервер не запущен";
            ServerStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed);
        }

        private void StartServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RemoteServerService.Start();
                _serverRunning = true;

                string ip = GetLocalIp();
                _currentUrl = $"http://{ip}:8080/";
                ServerStatus.Text = "Сервер запущен ✅";
                ServerStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);

                // Показываем QR-код
                GenerateQr(_currentUrl);
                QrPanel.Visibility = Visibility.Visible;
                ConnectUrl.Text = _currentUrl;
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
                QrPanel.Visibility = Visibility.Collapsed;

                ServerStatus.Text = "Сервер остановлен ⏹";
                ServerStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки сервера: {ex.Message}");
            }
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
                img.Freeze(); // важно: предотвращает утечку ресурсов и делает изображение пригодным для UI
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
