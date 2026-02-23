using InfoKioskApp.Models;
using InfoKioskApp.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class InterfaceSettingsView : UserControl
    {
        private readonly AppConfig _config;

        public InterfaceSettingsView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            _config.InterfaceSettings ??= new InterfaceSettings();
            _config.Ticker ??= new AppConfig.TickerSettings();
            _config.IdleScreen ??= new AppConfig.IdleScreenSettings();

            ThemeSelector.SelectedIndex = (_config.InterfaceSettings.Theme ?? "dark").ToLowerInvariant() == "light" ? 1 : 0;

            BackgroundColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.BackgroundColor ?? "#1E1E1E");
            ButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.ButtonBackground ?? "#3A3A3A");
            TextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.ButtonForeground ?? "White");
            NavButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.NavigationButtonBackground ?? "#3A3A3A");
            NavButtonTextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(_config.InterfaceSettings.NavigationButtonForeground ?? "White");

            FontFamilyTextBox.Text = _config.InterfaceSettings.FontFamily ?? "Segoe UI";
            FontSizeTextBox.Text = _config.InterfaceSettings.FontSize.ToString(CultureInfo.InvariantCulture);
            NavButtonFontSizeTextBox.Text = _config.InterfaceSettings.NavigationButtonFontSize.ToString(CultureInfo.InvariantCulture);

            FoodBlockIdTextBox.Text = _config.FoodBlockId ?? "15159";

            TickerEnabledCheck.IsChecked = _config.Ticker.Enabled;
            TickerTextBox.Text = _config.Ticker.Text ?? "";
            TickerSpeedTextBox.Text = _config.Ticker.Speed.ToString(CultureInfo.InvariantCulture);

            IdleEnabledCheck.IsChecked = _config.IdleScreen.Enabled;
            IdleLogoOnlyCheck.IsChecked = _config.IdleScreen.ShowLogoOnly;
            IdleTimeoutTextBox.Text = _config.IdleScreen.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            IdleSlideDurationTextBox.Text = _config.IdleScreen.SlideDurationSeconds.ToString(CultureInfo.InvariantCulture);
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            ApplyInterfaceColors(
                BackgroundColorPicker.SelectedColor?.ToString() ?? "#1E1E1E",
                ButtonColorPicker.SelectedColor?.ToString() ?? "#3A3A3A",
                TextColorPicker.SelectedColor?.ToString() ?? "White");
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ApplyInterfaceColors(
                BackgroundColorPicker.SelectedColor?.ToString() ?? "#1E1E1E",
                ButtonColorPicker.SelectedColor?.ToString() ?? "#3A3A3A",
                TextColorPicker.SelectedColor?.ToString() ?? "White");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _config.InterfaceSettings ??= new InterfaceSettings();
            _config.Ticker ??= new AppConfig.TickerSettings();
            _config.IdleScreen ??= new AppConfig.IdleScreenSettings();

            _config.InterfaceSettings.Theme = ((ThemeSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "dark").ToLowerInvariant();
            _config.InterfaceSettings.BackgroundColor = BackgroundColorPicker.SelectedColor?.ToString();
            _config.InterfaceSettings.ButtonBackground = ButtonColorPicker.SelectedColor?.ToString();
            _config.InterfaceSettings.ButtonForeground = TextColorPicker.SelectedColor?.ToString();
            _config.InterfaceSettings.NavigationButtonBackground = NavButtonColorPicker.SelectedColor?.ToString();
            _config.InterfaceSettings.NavigationButtonForeground = NavButtonTextColorPicker.SelectedColor?.ToString();
            _config.InterfaceSettings.FontFamily = string.IsNullOrWhiteSpace(FontFamilyTextBox.Text) ? "Segoe UI" : FontFamilyTextBox.Text.Trim();
            _config.InterfaceSettings.FontSize = ParseDouble(FontSizeTextBox.Text, 14);
            _config.InterfaceSettings.NavigationButtonFontSize = ParseDouble(NavButtonFontSizeTextBox.Text, 15);

            _config.FoodBlockId = string.IsNullOrWhiteSpace(FoodBlockIdTextBox.Text) ? "15159" : FoodBlockIdTextBox.Text.Trim();

            _config.Ticker.Enabled = TickerEnabledCheck.IsChecked == true;
            _config.Ticker.Text = TickerTextBox.Text ?? "";
            _config.Ticker.Speed = ParseDouble(TickerSpeedTextBox.Text, 1.5);

            _config.IdleScreen.Enabled = IdleEnabledCheck.IsChecked == true;
            _config.IdleScreen.ShowLogoOnly = IdleLogoOnlyCheck.IsChecked == true;
            _config.IdleScreen.TimeoutSeconds = ParseInt(IdleTimeoutTextBox.Text, 90);
            _config.IdleScreen.SlideDurationSeconds = ParseInt(IdleSlideDurationTextBox.Text, 8);

            ConfigService.SaveConfig(_config);
            MessageBox.Show("Настройки сохранены!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void ApplyInterfaceColors(string background, string buttonBg, string text)
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
            var tag = (ThemeSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (tag == "light")
            {
                BackgroundColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#F5F5F5");
                ButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#E0E0E0");
                TextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#222222");
                NavButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#E0E0E0");
                NavButtonTextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#222222");
            }
            else
            {
                BackgroundColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#1E1E1E");
                ButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#3A3A3A");
                TextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("White");
                NavButtonColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("#3A3A3A");
                NavButtonTextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString("White");
            }
        }

        private static int ParseInt(string? input, int fallback)
            => int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static double ParseDouble(string? input, double fallback)
            => double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
