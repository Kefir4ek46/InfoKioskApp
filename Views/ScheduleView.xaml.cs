using ClosedXML.Excel;
using InfoKioskApp.Services;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfoKioskApp.Views
{
    public partial class ScheduleView : UserControl
    {
        private Dictionary<string, string> _schedules;
        private string _activeScheduleKey;
        private string _activeSheetName;

        public ScheduleView()
        {
            InitializeComponent();
            LoadSchedules();
        }

        private void LoadSchedules()
        {
            var config = ConfigService.LoadConfig();

            _schedules = config.Schedules?.ToDictionary(s => s.Name, s => s.FilePath)
                          ?? new Dictionary<string, string>();

            ScheduleButtonsPanel.Children.Clear();

            foreach (var kvp in _schedules)
            {
                var btn = CreateButton(kvp.Key, (s, e) => LoadSchedule(kvp.Key, kvp.Value));
                ScheduleButtonsPanel.Children.Add(btn);
            }
        }

        private Button CreateButton(string text, RoutedEventHandler clickHandler)
        {
            var btn = new Button
            {
                Content = text,
                Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Foreground = Brushes.White,
                Margin = new Thickness(6),
                Padding = new Thickness(14, 8, 14, 8),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = text
            };

            btn.Click += clickHandler;
            return btn;
        }

        private void HighlightActiveButton(StackPanel panel, string key)
        {
            foreach (Button b in panel.Children)
            {
                bool isActive = (string)b.Tag == key;
                b.Background = new SolidColorBrush(isActive ? Color.FromRgb(58, 159, 255) : Color.FromRgb(68, 68, 68));
            }
        }

        private void LoadSchedule(string name, string path)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show($"Файл не найден:\n{path}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _activeScheduleKey = name;
            HighlightActiveButton(ScheduleButtonsPanel, name);

            string ext = Path.GetExtension(path).ToLower();

            if (ext == ".xlsx")
                LoadExcelSchedule(path);
            else if (ext == ".pdf")
                LoadPdfSchedule(path);
            else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                LoadImageSchedule(path);
            else
                MessageBox.Show("Неподдерживаемый формат файла.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        #region === Excel ===
        private void LoadExcelSchedule(string path)
        {
            try
            {
                using (var wb = new XLWorkbook(path))
                {
                    var sheetNames = wb.Worksheets.Select(ws => ws.Name).ToList();

                    ClassButtonsPanel.Children.Clear();
                    ClassButtonsPanel.Visibility = Visibility.Visible;

                    foreach (var sheet in sheetNames)
                    {
                        var btn = CreateButton(sheet, (s, e) => ShowExcelSheet(path, sheet));
                        ClassButtonsPanel.Children.Add(btn);
                    }

                    // Показываем первый лист по умолчанию
                    if (sheetNames.Any())
                        ShowExcelSheet(path, sheetNames[0]);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения Excel: {ex.Message}");
            }
        }

        
        #endregion


        #region === PDF ===
        private void LoadPdfSchedule(string path)
        {
            try
            {
                var viewer = new WebView2
                {
                    Source = new Uri(Path.GetFullPath(path)),
                    Margin = new Thickness(5)
                };

                ClassButtonsPanel.Visibility = Visibility.Collapsed;
                ContentArea.Children.Clear();
                ContentArea.Children.Add(viewer);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии PDF: {ex.Message}");
            }
        }
        private void ShowExcelSheet(string path, string sheetName)
        {
            _activeSheetName = sheetName;
            HighlightActiveButton(ClassButtonsPanel, sheetName);

            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet(sheetName);

                // Читаем все строки, включая пустые ячейки
                var data = new List<List<string>>();
                int maxColumns = 0;

                foreach (var row in ws.RowsUsed())
                {
                    var rowValues = new List<string>();
                    int lastUsedColumn = ws.LastColumnUsed().ColumnNumber();

                    for (int i = 1; i <= lastUsedColumn; i++)
                    {
                        var cell = row.Cell(i);
                        rowValues.Add(cell != null ? cell.Value.ToString() : string.Empty);

                    }

                    data.Add(rowValues);
                    if (rowValues.Count > maxColumns)
                        maxColumns = rowValues.Count;
                }

                // Если нет данных — выходим
                if (data.Count == 0)
                {
                    ContentArea.Children.Clear();
                    ContentArea.Children.Add(new TextBlock
                    {
                        Text = "Нет данных для отображения",
                        Foreground = Brushes.Gray,
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    return;
                }

                // === Контейнер прокрутки ===
                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
                };

                // === Таблица ===
                var grid = new Grid
                {
                    Margin = new Thickness(10)
                };

                for (int i = 0; i < maxColumns; i++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                for (int i = 0; i < data.Count; i++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // === Отрисовка ===
                for (int r = 0; r < data.Count; r++)
                {
                    for (int c = 0; c < maxColumns; c++)
                    {
                        bool isHeader = r == 0 || c == 0;

                        string cellText = c < data[r].Count ? data[r][c] : "";

                        var border = new Border
                        {
                            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                            BorderThickness = new Thickness(1),
                            Background = isHeader
                                ? new SolidColorBrush(Color.FromRgb(45, 45, 60))
                                : new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                            Padding = new Thickness(8),
                            Margin = new Thickness(2),
                            CornerRadius = new CornerRadius(isHeader ? 5 : 0)
                        };

                        var text = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(cellText) ? " " : cellText, // отображаем пустые
                            Foreground = Brushes.White,
                            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = isHeader ? 15 : 14,
                            TextWrapping = TextWrapping.Wrap
                        };

                        border.Child = text;
                        Grid.SetRow(border, r);
                        Grid.SetColumn(border, c);
                        grid.Children.Add(border);
                    }
                }

                scroll.Content = grid;
                ContentArea.Children.Clear();
                ContentArea.Children.Add(scroll);
            }
        }


        #endregion

        #region === Изображения ===
        private void LoadImageSchedule(string path)
        {
            try
            {
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(Path.GetFullPath(path))),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };

                ClassButtonsPanel.Visibility = Visibility.Collapsed;
                ContentArea.Children.Clear();
                ContentArea.Children.Add(img);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки изображения: {ex.Message}");
            }
        }
        #endregion
    }
}
