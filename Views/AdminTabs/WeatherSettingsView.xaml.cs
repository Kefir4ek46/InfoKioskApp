using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using InfoKioskApp.Services;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class WeatherSettingsView : UserControl
    {
        private AppConfig _config;

        public WeatherSettingsView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
            LoadSettings();
        }

        private void LoadSettings()
        {
            ShowWeatherCheckbox.IsChecked = _config.ShowWeather;
            ApiKeyInput.Text = _config.WeatherApiKey;
            CityIdInput.Text = _config.WeatherCityId;
        }

        private void SaveWeather_Click(object sender, RoutedEventArgs e)
        {
            _config.ShowWeather = ShowWeatherCheckbox.IsChecked ?? false;
            _config.WeatherApiKey = ApiKeyInput.Text.Trim();
            _config.WeatherCityId = CityIdInput.Text.Trim();

            ConfigService.SaveConfig(_config);
            MessageBox.Show("Настройки погоды сохранены!", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TestWeather_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string testUrl = $"https://api.meteogis.ru/weather/current?id={CityIdInput.Text.Trim()}&appid={ApiKeyInput.Text.Trim()}";
                var request = WebRequest.Create(testUrl);
                request.Method = "HEAD";

                using (var response = request.GetResponse())
                {
                    MessageBox.Show("Подключение успешно!", "Проверка", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch
            {
                MessageBox.Show("Ошибка при подключении. Проверьте API ключ и ID города.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
