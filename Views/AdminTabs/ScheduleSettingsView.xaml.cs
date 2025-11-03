using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using InfoKioskApp.Services;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class ScheduleSettingsView : UserControl
    {
        private AppConfig _config;

        public ScheduleSettingsView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
            LoadConfig();
        }

        private void LoadConfig()
        {
            MainSchedulePathBox.Text = _config.MainSchedulePath;
            ChangesPathBox.Text = _config.ChangesPath;
            ShowChangesBox.IsChecked = _config.ShowChanges;

            foreach (ComboBoxItem item in ChangesTypeBox.Items)
            {
                if ((string)item.Tag == _config.ChangesType)
                {
                    ChangesTypeBox.SelectedItem = item;
                    break;
                }
            }

            ExtraSchedulesList.ItemsSource = new List<ExtraSchedule>(_config.ExtraSchedules);
        }

        private void BrowseMain_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Excel файлы|*.xlsx" };
            if (dlg.ShowDialog() == true)
                MainSchedulePathBox.Text = dlg.FileName;
        }

        private void BrowseChanges_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Все поддерживаемые|*.xlsx;*.png;*.jpg;*.pdf|Excel|*.xlsx|Изображения|*.png;*.jpg|PDF|*.pdf" };
            if (dlg.ShowDialog() == true)
                ChangesPathBox.Text = dlg.FileName;
        }

        private void AddSchedule_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Поддерживаемые файлы|*.xlsx;*.png;*.jpg;*.pdf" };
            if (dlg.ShowDialog() == true)
            {
                string ext = Path.GetExtension(dlg.FileName).ToLower();
                string type;

                switch (ext)
                {
                    case ".xlsx":
                        type = "excel";
                        break;
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                        type = "image";
                        break;
                    case ".pdf":
                        type = "pdf";
                        break;
                    default:
                        type = "unknown";
                        break;
                }

                var schedule = new ExtraSchedule
                {
                    Name = Path.GetFileNameWithoutExtension(dlg.FileName),
                    Path = dlg.FileName,
                    Type = type
                };


                _config.ExtraSchedules.Add(schedule);
                ExtraSchedulesList.ItemsSource = null;
                ExtraSchedulesList.ItemsSource = new List<ExtraSchedule>(_config.ExtraSchedules);
            }
        }

        private void RemoveSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (ExtraSchedulesList.SelectedItem is ExtraSchedule schedule)
            {
                _config.ExtraSchedules.Remove(schedule);
                ExtraSchedulesList.ItemsSource = null;
                ExtraSchedulesList.ItemsSource = new List<ExtraSchedule>(_config.ExtraSchedules);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _config.MainSchedulePath = MainSchedulePathBox.Text;
            _config.ChangesPath = ChangesPathBox.Text;
            _config.ShowChanges = ShowChangesBox.IsChecked == true;

            if (ChangesTypeBox.SelectedItem is ComboBoxItem item)
                _config.ChangesType = item.Tag.ToString();

            ConfigService.SaveConfig(_config);
            MessageBox.Show("Настройки расписаний сохранены.", "InfoKioskApp",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
