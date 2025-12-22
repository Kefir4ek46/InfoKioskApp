using InfoKioskApp.Models;
using InfoKioskApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class CalendarSettingsView : UserControl
    {
        private readonly List<CalendarEvent> _events;

        public CalendarSettingsView()
        {
            InitializeComponent();
            _events = CalendarService.LoadEvents();
            RefreshList();
        }

        private void RefreshList()
        {
            EventsList.ItemsSource = null;
            EventsList.ItemsSource = _events.OrderBy(e => e.StartDate).ToList();
        }

        private void AddEvent_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text) || !StartDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Введите название и выберите дату начала.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var start = StartDatePicker.SelectedDate.Value;
            var end = EndDatePicker.SelectedDate ?? start;

            string type = (TypeBox.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "Выходной";

            _events.Add(new CalendarEvent
            {
                Title = TitleBox.Text.Trim(),
                StartDate = start,
                EndDate = end,
                Type = type
            });

            RefreshList();
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            if (EventsList.SelectedItem is CalendarEvent selected)
            {
                _events.Remove(selected);
                RefreshList();
            }
        }

        private void SaveEvents_Click(object sender, RoutedEventArgs e)
        {
            CalendarService.SaveEvents(_events);
            MessageBox.Show("События календаря сохранены.", "Инфо", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
