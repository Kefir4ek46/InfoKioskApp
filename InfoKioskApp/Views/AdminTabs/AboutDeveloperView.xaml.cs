using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class AboutDeveloperView : UserControl
    {
        private readonly string _aboutFilePath = "data/about_developer.txt";

        public AboutDeveloperView()
        {
            InitializeComponent();
            LoadAboutText();
        }

        private void LoadAboutText()
        {
            try
            {
                if (!File.Exists(_aboutFilePath))
                {
                    Directory.CreateDirectory("data");
                    File.WriteAllText(_aboutFilePath,
@"ИнфоКиоск — это современное WPF-приложение для отображения расписаний, медиа и документов.

👨‍💻 Разработчик: Maks
📧 Контакт: example@email.com
🗓 Версия: 1.0.0
🛠 Среда разработки: Visual Studio + .NET WPF
💡 Проект создан для автоматизации школьных информационных стендов.");

                }

                AboutText.Text = File.ReadAllText(_aboutFilePath);
            }
            catch (Exception ex)
            {
                AboutText.Text = $"Ошибка при загрузке информации: {ex.Message}";
            }
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            LoadAboutText();
        }
    }
}

