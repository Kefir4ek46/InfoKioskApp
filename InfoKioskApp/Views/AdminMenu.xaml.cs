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
        private void FilesSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new FilesSettingsView();
        }


        private void RemoteSettings_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new AdminTabs.RemoteAccessView();
        }

        private void ChangePin_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new ChangePinView();
        }

        private void AboutDeveloper_Click(object sender, RoutedEventArgs e)
        {
            ContentArea.Content = new AboutDeveloperView();
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
            // Сворачивает главное окно (MainWindow)
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
            }

            // Сворачивает также само админ-меню (чтобы не висело отдельно)
            this.WindowState = WindowState.Minimized;
        }

    }
}
