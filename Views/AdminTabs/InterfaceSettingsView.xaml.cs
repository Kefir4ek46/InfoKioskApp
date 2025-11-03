using InfoKioskApp.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InfoKioskApp.Models;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class InterfaceSettingsView : UserControl
    {
        private AppConfig _config;

        public InterfaceSettingsView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            if (_config.InterfaceSettings != null)
            {
                BackgroundColorPicker.SelectedColor =
                    (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.BackgroundColor ?? "#1E1E1E");
                ButtonColorPicker.SelectedColor =
                    (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.ButtonBackground ?? "#3A3A3A");
                TextColorPicker.SelectedColor =
                    (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.ButtonForeground ?? "White");
            }
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            ApplyInterfaceColors(
                BackgroundColorPicker.SelectedColor?.ToString() ?? "#1E1E1E",
                ButtonColorPicker.SelectedColor?.ToString() ?? "#3A3A3A",
                TextColorPicker.SelectedColor?.ToString() ?? "White"
            );
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ApplyInterfaceColors(
                BackgroundColorPicker.SelectedColor?.ToString(),
                ButtonColorPicker.SelectedColor?.ToString(),
                TextColorPicker.SelectedColor?.ToString()
            );
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_config.InterfaceSettings == null)
                _config.InterfaceSettings = new InterfaceSettings();

            _config.InterfaceSettings.BackgroundColor = BackgroundColorPicker.SelectedColor?.ToString();
            _config.InterfaceSettings.ButtonBackground = ButtonColorPicker.SelectedColor?.ToString();
            _config.InterfaceSettings.ButtonForeground = TextColorPicker.SelectedColor?.ToString();

            ConfigService.SaveConfig(_config);
            MessageBox.Show("Настройки сохранены и применены!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ApplyInterfaceColors(string background, string buttonBg, string text)
        {
            var app = Application.Current;

            app.Resources["AppBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
            app.Resources["PanelBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
            app.Resources["ButtonBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(buttonBg));
            app.Resources["ButtonForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
            app.Resources["TextForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeSelector.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                if (tag == "dark")
                {
                    BackgroundColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#1E1E1E");
                    ButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#444");
                    TextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("White");
                }
                else if (tag == "light")
                {
                    BackgroundColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#F5F5F5");
                    ButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#E0E0E0");
                    TextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#222");
                }

                ApplyInterfaceColors(
                    BackgroundColorPicker.SelectedColor?.ToString(),
                    ButtonColorPicker.SelectedColor?.ToString(),
                    TextColorPicker.SelectedColor?.ToString()
                );
            }
        }
    }
}
