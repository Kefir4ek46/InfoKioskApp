using InfoKioskApp.Views.AdminTabs;
using System.Windows;

namespace InfoKioskApp.Views
{
    public partial class AdminMenu : Window
    {
        public AdminMenu()
        {
            InitializeComponent();
            OpenSection("Интерфейс и темы", new InterfaceSettingsView());
        }

        private void OpenSection(string title, object view)
        {
            SectionTitleText.Text = title;
            ContentArea.Content = view;
        }

        private void InterfaceSettings_Click(object sender, RoutedEventArgs e)
            => OpenSection("Интерфейс и темы", new InterfaceSettingsView());

        private void BellSettings_Click(object sender, RoutedEventArgs e)
            => OpenSection("Расписание звонков", new BellSettingsView());

        private void WeatherSettings_Click(object sender, RoutedEventArgs e)
            => OpenSection("Погода", new WeatherSettingsView());

        private void CalendarSettings_Click(object sender, RoutedEventArgs e)
            => OpenSection("Календарь", new CalendarSettingsView());

        private void RemoteSettings_Click(object sender, RoutedEventArgs e)
            => OpenSection("Remote и доступ", new RemoteAccessView());

        private void ChangePin_Click(object sender, RoutedEventArgs e)
            => OpenSection("Смена PIN", new ChangePinView());

        private void AboutDeveloper_Click(object sender, RoutedEventArgs e)
            => OpenSection("О разработчике", new AboutDeveloperView());

        private void Logout_Click(object sender, RoutedEventArgs e)
            => Close();

        private void Exit_Click(object sender, RoutedEventArgs e)
            => Application.Current?.Shutdown();

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current?.MainWindow != null)
                Application.Current.MainWindow.WindowState = WindowState.Minimized;

            WindowState = WindowState.Minimized;
        }
    }
}
