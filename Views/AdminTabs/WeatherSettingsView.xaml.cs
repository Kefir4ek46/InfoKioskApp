using System;
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
            LoadConfig();
        }

        private void LoadConfig()
        {
            var config = ConfigService.LoadConfig();

            CityBox.Text = config.City ?? "";
            LatitudeBox.Text = config.CachedLatitude != 0 ? config.CachedLatitude.ToString() : "";
            LongitudeBox.Text = config.CachedLongitude != 0 ? config.CachedLongitude.ToString() : "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigService.LoadConfig();

            config.City = CityBox.Text.Trim();

            if (double.TryParse(LatitudeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double lat))
                config.CachedLatitude = lat;

            if (double.TryParse(LongitudeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double lon))
                config.CachedLongitude = lon;

            ConfigService.SaveConfig(config);

            MessageBox.Show("Настройки погоды сохранены ✅\nПроверьте отображение на главном экране.",
                "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

