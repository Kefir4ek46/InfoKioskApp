using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using InfoKioskApp.Services;
using ClosedXML.Excel;
using static InfoKioskApp.Services.ConfigService;
using System.Windows.Media;

namespace InfoKioskApp.Views
{
    public partial class ScheduleView : UserControl
    {
        private readonly AppConfig _config;

        public ScheduleView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
            InitializeTabs();
        }

        private void InitializeTabs()
        {
            // 1. Основное расписание
            ScheduleTabs.Items.Add(new TabItem { Header = "Основное расписание", Tag = "main" });

            // 2. Изменения (если включено)
            if (_config.ShowChanges)
                ScheduleTabs.Items.Add(new TabItem { Header = "Изменения на сегодня", Tag = "changes" });

            // 3. Дополнительные расписания
            foreach (var extra in _config.ExtraSchedules)
                ScheduleTabs.Items.Add(new TabItem { Header = extra.Name, Tag = extra });

            // Подписываемся на событие
            ScheduleTabs.SelectionChanged += ScheduleTabs_SelectionChanged;

            // По умолчанию — первая вкладка
            ((TabItem)ScheduleTabs.Items[0]).IsSelected = true;
            ShowMainSchedule();
        }


        private void ShowMainSchedule()
        {
            ScheduleContent.Children.Clear();

            if (!File.Exists(_config.MainSchedulePath))
            {
                ScheduleContent.Children.Add(new TextBlock
                {
                    Text = "Файл основного расписания не найден.",
                    Foreground = Brushes.Red,
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            // Загружаем классы (названия листов)
            var workbook = new XLWorkbook(_config.MainSchedulePath);
            var classList = workbook.Worksheets.Select(ws => ws.Name).ToList();

            var combo = new ComboBox
            {
                ItemsSource = classList,
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 10)
            };
            combo.SelectionChanged += (s, e) =>
            {
                string sheetName = combo.SelectedItem.ToString();
                ShowExcelSchedule(workbook.Worksheet(sheetName));
            };

            var panel = new StackPanel();
            panel.Children.Add(combo);

            // Показ первого по умолчанию
            ShowExcelSchedule(workbook.Worksheet(classList[0]), panel);

            ScheduleContent.Children.Add(panel);
        }

        private void ShowExcelSchedule(IXLWorksheet sheet, Panel parent = null)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = true,
                ItemsSource = sheet.RowsUsed().Select(r => r.Cells().Select(c => c.Value.ToString()).ToList()).ToList()
            };

            if (parent != null)
                parent.Children.Add(grid);
            else
            {
                ScheduleContent.Children.Clear();
                ScheduleContent.Children.Add(grid);
            }
        }
        private void ScheduleTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScheduleTabs.SelectedItem is TabItem selectedTab) switch (selectedTab.Tag)
                {
                    case "main":
                        ShowMainSchedule();
                        break;
                    case "changes":
                        ShowChangesSchedule();
                        break;
                    case ExtraSchedule extra:
                        ShowExtraSchedule(extra);
                        break;
                }
        }


        private void ShowChangesSchedule()
        {
            ScheduleContent.Children.Clear();
            string path = _config.ChangesPath;

            if (!File.Exists(path))
            {
                ScheduleContent.Children.Add(new TextBlock
                {
                    Text = "Файл изменений не найден.",
                    Foreground = Brushes.Red,
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            if (_config.ChangesType == "image")
            {
                var img = new Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(path))),
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
                ScheduleContent.Children.Add(img);
            }
            else if (_config.ChangesType == "excel")
            {
                var wb = new XLWorkbook(path);
                var ws = wb.Worksheets.First();
                ShowExcelSchedule(ws);
            }
            else if (_config.ChangesType == "pdf")
            {
                var txt = new TextBlock
                {
                    Text = "Поддержка PDF будет добавлена позже.",
                    Foreground = Brushes.Gray,
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                ScheduleContent.Children.Add(txt);
            }
        }

        private void ShowExtraSchedule(ExtraSchedule extra)
        {
            ScheduleContent.Children.Clear();

            if (!File.Exists(extra.Path))
            {
                ScheduleContent.Children.Add(new TextBlock
                {
                    Text = $"Файл для «{extra.Name}» не найден.",
                    Foreground = Brushes.Red,
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            switch (extra.Type)
            {
                case "image":
                    ScheduleContent.Children.Add(new Image
                    {
                        Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(extra.Path))),
                        Stretch = System.Windows.Media.Stretch.Uniform
                    });
                    break;
                case "excel":
                    var wb = new XLWorkbook(extra.Path);
                    var ws = wb.Worksheets.First();
                    ShowExcelSchedule(ws);
                    break;
                case "pdf":
                    ScheduleContent.Children.Add(new TextBlock
                    {
                        Text = "PDF пока не поддерживается.",
                        Foreground = Brushes.Gray,
                        FontSize = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    break;
            }
        }
    }
}
