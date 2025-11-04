using InfoKioskApp.Models;
using InfoKioskApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfoKioskApp.Views
{
    public partial class CalendarView : UserControl
    {
        private List<CalendarEvent> _events;
        private DateTime _currentMonth;

        public CalendarView()
        {
            InitializeComponent();
            _currentMonth = DateTime.Now;
            LoadEvents();
            BuildCalendar();
        }

        private void LoadEvents()
        {
            _events = CalendarService.LoadEvents() ?? new List<CalendarEvent>();
        }

        private void BuildCalendar()
        {
            CalendarGrid.Children.Clear();
            MonthLabel.Text = _currentMonth.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));

            DateTime firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(_currentMonth.Year, _currentMonth.Month);

            int startOffset = ((int)firstDay.DayOfWeek + 6) % 7;

            for (int i = 0; i < startOffset; i++)
                CalendarGrid.Children.Add(new Border());

            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime currentDate = new DateTime(_currentMonth.Year, _currentMonth.Month, day);

                // ✅ Сравниваем только дату, без времени
                var dayEvents = _events
                    .Where(e =>
                        currentDate.Date >= e.StartDate.Date &&
                        currentDate.Date <= e.EndDate.Date)
                    .ToList();

                var border = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(3),
                    Padding = new Thickness(6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    Background = CreateDayBackground(currentDate, dayEvents),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var text = new TextBlock
                {
                    Text = day.ToString(),
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                    FontWeight = dayEvents.Any() ? FontWeights.Bold : FontWeights.Normal
                };

                border.Child = text;

                border.MouseLeftButtonUp += (s, e) =>
                {
                    if (dayEvents.Any())
                        ShowEventDialog(currentDate, dayEvents);
                    else
                        MessageBox.Show($"{currentDate:dd MMMM yyyy}\nНет событий", "Календарь",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                };

                CalendarGrid.Children.Add(border);
            }
        }

        private Brush CreateDayBackground(DateTime date, List<CalendarEvent> dayEvents)
        {
            // выходные без событий
            if (!dayEvents.Any() && (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday))
                return new SolidColorBrush(Color.FromRgb(55, 55, 75));

            // без событий
            if (dayEvents.Count == 0)
                return new SolidColorBrush(Color.FromRgb(45, 45, 45));

            // одно событие
            if (dayEvents.Count == 1)
                return new SolidColorBrush(GetColorForTypeRaw(dayEvents[0].Type));

            // несколько событий → рисуем многоцветную полосу
            var distinctTypes = dayEvents.Select(e => e.Type).Distinct().ToList();
            var drawingGroup = new DrawingGroup();
            double heightPer = 1.0 / distinctTypes.Count;

            for (int i = 0; i < distinctTypes.Count; i++)
            {
                var color = GetColorForTypeRaw(distinctTypes[i]);
                var rect = new GeometryDrawing(
                    new SolidColorBrush(color),
                    null,
                    new RectangleGeometry(new Rect(0, i * heightPer, 1, heightPer))
                );
                drawingGroup.Children.Add(rect);
            }

            var brush = new DrawingBrush(drawingGroup)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
                Viewport = new Rect(0, 0, 1, 1),
                Viewbox = new Rect(0, 0, 1, 1),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
            };
            brush.Freeze(); // ✅ фиксируем кисть для корректного рендера

            return brush;
        }


                 

        private Color GetColorForTypeRaw(string type)
        {
            switch (type)
            {
                case "Выходной": return Color.FromRgb(30, 80, 180);
                case "Праздник": return Color.FromRgb(180, 40, 40);
                case "Каникулы": return Color.FromRgb(40, 150, 40);
                case "Другое": return Color.FromRgb(0, 150, 150);
                default: return Color.FromRgb(70, 70, 90);
            }
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(-1);
            BuildCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(1);
            BuildCalendar();
        }

        private void ShowEventDialog(DateTime date, List<CalendarEvent> events)
        {
            var dialog = new Window
            {
                Title = $"События {date:dd MMMM yyyy}",
                Width = 420,
                Height = 320,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(15) };

            foreach (var e in events)
            {
                var eventPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 5, 0, 5)
                };

                var colorRect = new Border
                {
                    Width = 16,
                    Height = 16,
                    Background = new SolidColorBrush(GetColorForTypeRaw(e.Type)),
                    Margin = new Thickness(0, 0, 10, 0),
                    CornerRadius = new CornerRadius(3)
                };

                var title = new TextBlock
                {
                    Text = $"{e.Title} ({e.Type})",
                    VerticalAlignment = VerticalAlignment.Center
                };

                eventPanel.Children.Add(colorRect);
                eventPanel.Children.Add(title);
                stack.Children.Add(eventPanel);
            }

            var closeBtn = new Button
            {
                Content = "Закрыть",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(60, 100, 200)),
                Foreground = Brushes.White
            };
            closeBtn.Click += (s, e) => dialog.Close();

            stack.Children.Add(closeBtn);
            dialog.Content = stack;
            dialog.ShowDialog();
        }
    }
}
