namespace InfoKioskApp.Models
{
    public class InterfaceSettings
    {
        public string Theme { get; set; } = "dark";
        public string BackgroundColor { get; set; } = "#1E1E1E";
        public string ButtonBackground { get; set; } = "#3A3A3A";
        public string ButtonForeground { get; set; } = "White";

        public string NavigationButtonBackground { get; set; } = "#3A3A3A";
        public string NavigationButtonForeground { get; set; } = "White";

        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 14;
        public double NavigationButtonFontSize { get; set; } = 15;
    }
}
