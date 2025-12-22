using InfoKioskApp.Services;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class ChangePinView : UserControl
    {
        public ChangePinView()
        {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigService.LoadConfig();

            string oldPin = OldPinBox.Password?.Trim() ?? "";
            string newPin = NewPinBox.Password?.Trim() ?? "";
            string confirmPin = ConfirmPinBox.Password?.Trim() ?? "";

            if (oldPin != config.PinCode)
            {
                MessageBox.Show("Текущий PIN введён неверно!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(newPin) || newPin.Length < 3)
            {
                MessageBox.Show("Новый PIN должен содержать минимум 3 символа.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newPin != confirmPin)
            {
                MessageBox.Show("Новый PIN и подтверждение не совпадают.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            config.PinCode = newPin;
            ConfigService.SaveConfig(config);

            MessageBox.Show("PIN-код успешно изменён ✅", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Можно очистить поля или просто вывести сообщение
            OldPinBox.Password = "";
            NewPinBox.Password = "";
            ConfirmPinBox.Password = "";
        }
    }
}
