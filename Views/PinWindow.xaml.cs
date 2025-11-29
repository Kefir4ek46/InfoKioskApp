using System.Windows;
using InfoKioskApp.Models;
using InfoKioskApp.Services;

namespace InfoKioskApp.Views
{
    public partial class PinWindow : Window
    {
        private readonly AppConfig _config;

        public bool IsAuthorized { get; private set; } = false;

        public PinWindow()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig(); // ✅ прямой вызов статического метода
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string input = PinInput.Password.Trim();

            // ✅ Проверяем введённый пин
            if (input == _config.PinCode)
            {
                IsAuthorized = true;
                DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Неверный PIN-код!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                PinInput.Clear();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }
    }
}
