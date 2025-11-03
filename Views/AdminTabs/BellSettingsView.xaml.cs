using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json; // <-- убедись, что пакет установлен

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class BellSettingsView : UserControl
    {
        private List<Bell> _bells = new List<Bell>();
        private readonly string _path = "data\\BellSchedule.json";

        public BellSettingsView()
        {
            InitializeComponent();
            LoadBells();
        }

        private void LoadBells()
        {
            try
            {
                if (File.Exists(_path))
                {
                    string json = File.ReadAllText(_path);
                    _bells = JsonConvert.DeserializeObject<List<Bell>>(json) ?? new List<Bell>();
                }
                else
                {
                    _bells = new List<Bell>();
                }

                BellsGrid.ItemsSource = _bells;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке расписания звонков: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddBell_Click(object sender, RoutedEventArgs e)
        {
            _bells.Add(new Bell { Number = _bells.Count + 1, Start = "08:30", End = "09:15" });
            BellsGrid.Items.Refresh();
        }

        private void RemoveBell_Click(object sender, RoutedEventArgs e)
        {
            if (BellsGrid.SelectedItem is Bell bell)
            {
                _bells.Remove(bell);
                // перенумеровываем
                for (int i = 0; i < _bells.Count; i++)
                    _bells[i].Number = i + 1;
                BellsGrid.Items.Refresh();
            }
        }

        private void SaveBells_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory("data");
                string json = JsonConvert.SerializeObject(_bells, Formatting.Indented);
                File.WriteAllText(_path, json);
                MessageBox.Show("Расписание звонков сохранено!", "Сохранено", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при сохранении расписания: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Простая модель звонка. Если у тебя уже есть Bell в другом неймспейсе — удали этот класс или приведите к одному неймспейсу.
    public class Bell
    {
        public int Number { get; set; }
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }
}
