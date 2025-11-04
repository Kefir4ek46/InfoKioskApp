using InfoKioskApp.Models;
using InfoKioskApp.Services;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class ScheduleSettingsView : UserControl
    {
        private List<ScheduleFile> _schedules;

        public ScheduleSettingsView()
        {
            InitializeComponent();
            LoadSchedules();
        }

        private void LoadSchedules()
        {
            var config = ConfigService.LoadConfig();
            _schedules = config.Schedules ?? new List<ScheduleFile>();
            RefreshList();
        }

        private void RefreshList()
        {
            ScheduleList.ItemsSource = null;
            ScheduleList.ItemsSource = _schedules;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Все поддерживаемые|*.xlsx;*.xls;*.pdf;*.png;*.jpg;*.jpeg|Excel|*.xlsx;*.xls|PDF|*.pdf|Изображения|*.png;*.jpg;*.jpeg"
            };
            if (dlg.ShowDialog() == true)
                PathBox.Text = dlg.FileName;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(PathBox.Text))
            {
                MessageBox.Show("Введите название и выберите файл.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string type = (TypeBox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower() ?? "excel";

            var existing = _schedules.FirstOrDefault(x => x.Name == NameBox.Text);
            if (existing != null)
            {
                existing.FilePath = PathBox.Text;
                existing.Type = type;
            }
            else
            {
                _schedules.Add(new ScheduleFile
                {
                    Name = NameBox.Text,
                    FilePath = PathBox.Text,
                    Type = type
                });
            }

            RefreshList();
            NameBox.Text = "";
            PathBox.Text = "";
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ScheduleFile file)
            {
                NameBox.Text = file.Name;
                PathBox.Text = file.FilePath;
                TypeBox.SelectedItem = TypeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Content == file.Type);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ScheduleFile file)
            {
                if (MessageBox.Show($"Удалить расписание «{file.Name}»?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _schedules.Remove(file);
                    RefreshList();
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigService.LoadConfig();
            config.Schedules = _schedules;
            ConfigService.SaveConfig(config);

            MessageBox.Show("Расписания сохранены ✅", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
