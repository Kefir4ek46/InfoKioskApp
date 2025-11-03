using System.Windows;
using InfoKioskApp.Views.AdminTabs;

namespace InfoKioskApp.Views
{
    public partial class AdminMenu : Window
    {
        public AdminMenu()
        {
            InitializeComponent();
        }

        private void InterfaceSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new InterfaceSettingsView();
        }

        private void ScheduleSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new ScheduleSettingsView();
        }

        private void BellSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new BellSettingsView();
        }

        private void WeatherSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new WeatherSettingsView();
        }

        private void CalendarSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new CalendarSettingsView();
        }

        private void RemoteSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new RemoteSettingsView();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
    }
}
