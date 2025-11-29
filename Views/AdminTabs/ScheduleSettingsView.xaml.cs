using InfoKioskApp.Models;
using InfoKioskApp.Services;
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class ScheduleSettingsView : UserControl
    {
        private List<AppConfig.ScheduleItem> _others = [];


        public ScheduleSettingsView()
        {
            InitializeComponent();
            LoadConfig();
        }

        private void LoadConfig()
        {
            var config = ConfigService.LoadConfig();
            MainScheduleBox.Text = config.MainSchedulePath;
            ChangesBox.Text = config.ChangesPath;

            _others = config.Schedules ?? [];
            RefreshOtherList();
        }
        private void DeleteOther_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.CommandParameter is not string selected) return;

            var item = _others.FirstOrDefault(x => $"{x.Name} — {x.FilePath}" == selected);
            if (item != null)
            {
                if (MessageBox.Show($"Удалить расписание '{item.Name}'?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _others.Remove(item);
                    RefreshOtherList();
                }
            }
        }

        private void RefreshOtherList()
        {
            OtherSchedulesList.ItemsSource = null;
            OtherSchedulesList.ItemsSource = _others.Select(s => $"{s.Name} — {s.FilePath}").ToList();
        }

        private void BrowseMain_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Excel|*.xlsx;*.xls" };
            if (dlg.ShowDialog() == true)
                MainScheduleBox.Text = dlg.FileName;
        }

        private void BrowseChanges_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Поддерживаемые файлы|*.xlsx;*.xls;*.pdf;*.png;*.jpg;*.jpeg;*.docx"
            };
            if (dlg.ShowDialog() == true)
                ChangesBox.Text = dlg.FileName;
        }

        private void BrowseOther_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Все поддерживаемые|*.xlsx;*.xls;*.pdf;*.png;*.jpg;*.jpeg;*.docx"
            };
            if (dlg.ShowDialog() == true)
                OtherPathBox.Text = dlg.FileName;
        }

        private void AddOther_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(OtherNameBox.Text) || string.IsNullOrWhiteSpace(OtherPathBox.Text))
            {
                MessageBox.Show("Введите название и выберите файл.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = _others.FirstOrDefault(x => x.Name == OtherNameBox.Text);
            if (existing != null)
            {
                existing.FilePath = OtherPathBox.Text;
            }
            else
            {
                _others.Add(new AppConfig.ScheduleItem
                {
                    Name = OtherNameBox.Text,
                    FilePath = OtherPathBox.Text
                });
            }

            OtherNameBox.Clear();
            OtherPathBox.Clear();
            RefreshOtherList();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigService.LoadConfig();
            config.MainSchedulePath = MainScheduleBox.Text;
            config.ChangesPath = ChangesBox.Text;
            config.Schedules = _others;
            ConfigService.SaveConfig(config);

            MessageBox.Show("Настройки расписаний сохранены ✅", "Успешно",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
