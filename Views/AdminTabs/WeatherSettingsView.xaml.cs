using System.Windows;
using System.Windows.Controls;
using InfoKioskApp.Services;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class WeatherSettingsView : UserControl
    {
        public WeatherSettingsView()
        {
            InitializeComponent();
            var config = ConfigService.LoadConfig();
            CityBox.Text = config.City;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigService.LoadConfig();
            config.City = CityBox.Text;
            ConfigService.SaveConfig(config);

            MessageBox.Show("Настройки сохранены ✅\nПогода обновится при следующем запуске.",
                "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
