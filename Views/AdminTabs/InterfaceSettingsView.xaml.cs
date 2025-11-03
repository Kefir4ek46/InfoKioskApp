using System.Windows;
using System.Windows.Controls;
using InfoKioskApp.Services;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class InterfaceSettingsView : UserControl
    {
        private AppConfig _config;

        public InterfaceSettingsView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();

            // Применяем текущие настройки
            ThemeSelector.SelectedIndex = _config.Theme == "light" ? 1 : 0;
            FontSelector.SelectedItem = FindFontItem(_config.FontFamily);
            FontSizeSlider.Value = _config.FontSize;
            FontSizeValue.Text = _config.FontSize.ToString();
        }

        private ComboBoxItem FindFontItem(string fontName)
        {
            foreach (ComboBoxItem item in FontSelector.Items)
                if (item.Content.ToString() == fontName)
                    return item;
            return null;
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeSelector.SelectedItem is ComboBoxItem item)
                _config.Theme = item.Tag.ToString();
        }

        private void FontSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontSelector.SelectedItem is ComboBoxItem item)
                _config.FontFamily = item.Content.ToString();
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeValue != null)
                FontSizeValue.Text = ((int)e.NewValue).ToString();

            _config.FontSize = (int)e.NewValue;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ConfigService.SaveConfig(_config);
            MessageBox.Show("Настройки интерфейса сохранены.", "InfoKioskApp",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

