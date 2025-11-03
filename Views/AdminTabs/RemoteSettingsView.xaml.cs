using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views
{
    public partial class RemoteSettingsView : UserControl
    {
        private bool _serverRunning = false;

        public RemoteSettingsView()
        {
            InitializeComponent();
        }

        private void StartServer_Click(object sender, RoutedEventArgs e)
        {
            _serverRunning = true;
            ServerStatusText.Text = $"Сервер запущен на {IpAddressBox.Text}:{PortBox.Text}";
            ServerStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
        }

        private void StopServer_Click(object sender, RoutedEventArgs e)
        {
            _serverRunning = false;
            ServerStatusText.Text = "Сервер остановлен";
            ServerStatusText.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }
}
