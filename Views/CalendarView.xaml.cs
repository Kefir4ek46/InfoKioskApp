using System;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views
{
    public partial class CalendarView : UserControl
    {
        public CalendarView()
        {
            InitializeComponent();
            MainCalendar.SelectedDatesChanged += MainCalendar_SelectedDateChanged;
        }

        private void MainCalendar_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainCalendar.SelectedDate.HasValue)
            {
                DateTime selectedDate = MainCalendar.SelectedDate.Value;
                SelectedDateText.Text = $"Вы выбрали: {selectedDate:dd MMMM yyyy}";
            }
        }
    }
}
