using System.Windows;
using System.Windows.Media;

namespace InfoKioskApp
{
    public partial class App : Application
    {
        public static void SetTheme(string theme)
        {
            ResourceDictionary newTheme = [];

            if (theme == "light")
            {
                newTheme.Add("BackgroundColor", (Color)ColorConverter.ConvertFromString("#F0F0F0"));
                newTheme.Add("PanelColor", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
                newTheme.Add("TextColor", (Color)ColorConverter.ConvertFromString("#000000"));
                newTheme.Add("ButtonColor", (Color)ColorConverter.ConvertFromString("#E0E0E0"));
            }
            else // dark
            {
                newTheme.Add("BackgroundColor", (Color)ColorConverter.ConvertFromString("#1E1E1E"));
                newTheme.Add("PanelColor", (Color)ColorConverter.ConvertFromString("#2C2C2C"));
                newTheme.Add("TextColor", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
                newTheme.Add("ButtonColor", (Color)ColorConverter.ConvertFromString("#3A3A3A"));
            }

            // Обновляем ресурсы приложения
            Current.Resources.MergedDictionaries.Clear();
            Current.Resources.MergedDictionaries.Add(newTheme);
        }
    }
}
