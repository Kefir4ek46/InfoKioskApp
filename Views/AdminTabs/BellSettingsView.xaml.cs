using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class BellSettingsView : UserControl
    {
        private ObservableCollection<BellItem> Bells { get; set; }
        private readonly string bellsFile = "data/BellSchedule.json";

        public BellSettingsView()
        {
            InitializeComponent();
            LoadBells();
        }

        private void LoadBells()
        {
            if (File.Exists(bellsFile))
            {
                string json = File.ReadAllText(bellsFile);
                Bells = JsonConvert.DeserializeObject<ObservableCollection<BellItem>>(json);
            }
            else
            {
                Bells = new ObservableCollection<BellItem>();
            }

            if (Bells == null)
                Bells = new ObservableCollection<BellItem>();

            BellsGrid.ItemsSource = Bells;
        }

        private void AddBell_Click(object sender, RoutedEventArgs e)
        {
            int duration = int.Parse(((ComboBoxItem)LessonDurationCombo.SelectedItem).Content.ToString());
            TimeSpan startTime;

            if (Bells.Count == 0)
            {
                startTime = TimeSpan.Parse(StartTimeBox.Text);
            }
            else
            {
                var last = Bells[Bells.Count - 1];
                startTime = TimeSpan.Parse(last.End).Add(TimeSpan.FromMinutes(last.BreakAfter));
            }

            TimeSpan endTime = startTime.Add(TimeSpan.FromMinutes(duration));

            Bells.Add(new BellItem
            {
                Number = Bells.Count + 1,
                Start = startTime.ToString(@"hh\:mm"),
                End = endTime.ToString(@"hh\:mm"),
                BreakAfter = 10
            });
        }

        private void RemoveBell_Click(object sender, RoutedEventArgs e)
        {
            if (BellsGrid.SelectedItem is BellItem selected)
                Bells.Remove(selected);

            for (int i = 0; i < Bells.Count; i++)
                Bells[i].Number = i + 1;
        }

        private void Recalculate_Click(object sender, RoutedEventArgs e)
        {
            if (Bells.Count == 0) return;

            int duration = int.Parse(((ComboBoxItem)LessonDurationCombo.SelectedItem).Content.ToString());
            TimeSpan currentStart = TimeSpan.Parse(StartTimeBox.Text);

            foreach (var bell in Bells)
            {
                TimeSpan end = currentStart.Add(TimeSpan.FromMinutes(duration));
                bell.Start = currentStart.ToString(@"hh\:mm");
                bell.End = end.ToString(@"hh\:mm");
                currentStart = end.Add(TimeSpan.FromMinutes(bell.BreakAfter));
            }

            BellsGrid.Items.Refresh();
        }

        private void SaveBells_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < Bells.Count; i++)
                Bells[i].Number = i + 1;

            string json = JsonConvert.SerializeObject(Bells, Formatting.Indented);
            File.WriteAllText(bellsFile, json);

            MessageBox.Show("Расписание звонков сохранено!", "InfoKioskApp",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public class BellItem
    {
        public int Number { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public int BreakAfter { get; set; }
    }
}
