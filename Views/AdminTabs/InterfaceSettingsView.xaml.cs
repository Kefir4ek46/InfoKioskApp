using InfoKioskApp.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InfoKioskApp.Models;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class InterfaceSettingsView : UserControl
    {
        public InterfaceSettingsView()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var config = ConfigService.LoadConfig();
            if (config.InterfaceSettings != null)
            {
                BackgroundColorPicker.Text = config.InterfaceSettings.BackgroundColor ?? "#1E1E1E";
                ButtonColorPicker.Text = config.InterfaceSettings.ButtonBackground ?? "#3A3A3A";
                TextColorPicker.Text = config.InterfaceSettings.ButtonForeground ?? "White";
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ApplyInterfaceColors(BackgroundColorPicker.Text, ButtonColorPicker.Text, TextColorPicker.Text);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigService.LoadConfig();
            if (config.InterfaceSettings == null)
                config.InterfaceSettings = new InterfaceSettings();

            config.InterfaceSettings.BackgroundColor = BackgroundColorPicker.Text;
            config.InterfaceSettings.ButtonBackground = ButtonColorPicker.Text;
            config.InterfaceSettings.ButtonForeground = TextColorPicker.Text;

            ConfigService.SaveConfig(config);

            ApplyInterfaceColors(
                config.InterfaceSettings.BackgroundColor,
                config.InterfaceSettings.ButtonBackground,
                config.InterfaceSettings.ButtonForeground
            );

            MessageBox.Show("Настройки сохранены и применены!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ApplyInterfaceColors(string background, string buttonBg, string text)
        {
            var app = Application.Current;

            SolidColorBrush bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
            SolidColorBrush btnBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(buttonBg));
            SolidColorBrush textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));

            app.Resources["AppBackgroundBrush"] = bgBrush;
            app.Resources["PanelBackgroundBrush"] = bgBrush;
            app.Resources["ButtonBackgroundBrush"] = btnBgBrush;
            app.Resources["ButtonForegroundBrush"] = textBrush;
            app.Resources["TextForegroundBrush"] = textBrush;
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeSelector.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                if (tag == "dark")
                {
                    BackgroundColorPicker.Text = "#1E1E1E";
                    ButtonColorPicker.Text = "#444";
                    TextColorPicker.Text = "White";
                }
                else if (tag == "light")
                {
                    BackgroundColorPicker.Text = "#F5F5F5";
                    ButtonColorPicker.Text = "#E0E0E0";
                    TextColorPicker.Text = "#222";
                }

                ApplyInterfaceColors(BackgroundColorPicker.Text, ButtonColorPicker.Text, TextColorPicker.Text);
            }
        }
    }
}
