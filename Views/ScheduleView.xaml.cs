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

// === Для DOCX (Open XML SDK) ===
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

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

            _schedules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (config.Schedules != null)
            {
                foreach (var s in config.Schedules)
                {
                    if (string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.FilePath))
                        continue;

                    if (!_schedules.ContainsKey(s.Name))
                        _schedules[s.Name] = s.FilePath;
                    else
                        Console.WriteLine($"⚠ Пропущено дубликатное расписание: {s.Name}");
                }
            }


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
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(68, 68, 68)),
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
                b.Background = new SolidColorBrush(isActive ? System.Windows.Media.Color.FromRgb(58, 159, 255) : System.Windows.Media.Color.FromRgb(68, 68, 68));
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
            else if (ext == ".docx")
                LoadDocxSchedule(path);
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

        private void ShowExcelSheet(string path, string sheetName)
        {
            _activeSheetName = sheetName;
            HighlightActiveButton(ClassButtonsPanel, sheetName);

            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet(sheetName);

                // Соберём используемые ячейки — но хотим отображать пустые ячейки тоже.
                // Определим максимальную используемую колонку и строку в листе.
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

                // Если нет данных
                if (lastRow == 0 || lastCol == 0)
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
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
                };

                // === Таблица с равными колонками ===
                var grid = new Grid
                {
                    Margin = new Thickness(10)
                };

                // Равные ширины колонок
                for (int c = 0; c < lastCol; c++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                for (int r = 0; r < lastRow; r++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Чтение всех ячеек, включая пустые
                for (int r = 1; r <= lastRow; r++)
                {
                    for (int c = 1; c <= lastCol; c++)
                    {
                        var cell = ws.Cell(r, c);
                        string cellText = (cell == null || cell.IsEmpty()) ? string.Empty : cell.GetString();

                        bool isHeader = r == 1 || c == 1;

                        var border = new System.Windows.Controls.Border
                        {
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                            BorderThickness = new Thickness(1),
                            Background = isHeader
                                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 60))
                                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35)),
                            Padding = new Thickness(8),
                            Margin = new Thickness(2),
                            CornerRadius = new CornerRadius(isHeader ? 5 : 0)
                        };

                        var text = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(cellText) ? " " : cellText,
                            Foreground = Brushes.White,
                            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                            TextAlignment = System.Windows.TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = isHeader ? 15 : 14,
                            TextWrapping = TextWrapping.Wrap
                        };

                        border.Child = text;
                        Grid.SetRow(border, r - 1);
                        Grid.SetColumn(border, c - 1);
                        grid.Children.Add(border);
                    }
                }

                scroll.Content = grid;
                ContentArea.Children.Clear();
                ContentArea.Children.Add(scroll);
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
        #endregion

        #region === DOCX (Word) ===
        private void LoadDocxSchedule(string path)
        {
            try
            {
                // Container with scroll
                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
                };

                var stack = new StackPanel
                {
                    Margin = new Thickness(12),
                    Orientation = Orientation.Vertical
                };

                using (var doc = WordprocessingDocument.Open(path, false))
                {
                    var body = doc.MainDocumentPart?.Document?.Body;
                    if (body == null)
                    {
                        ContentArea.Children.Clear();
                        ContentArea.Children.Add(new TextBlock
                        {
                            Text = "Документ пуст.",
                            Foreground = Brushes.Gray,
                            FontSize = 16
                        });
                        return;
                    }

                    foreach (var element in body.Elements())
                    {
                        if (element is Paragraph p)
                        {
                            var text = GetParagraphText(p);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                var tb = new TextBlock
                                {
                                    Text = text,
                                    Foreground = Brushes.White,
                                    TextWrapping = TextWrapping.Wrap,
                                    Margin = new Thickness(0, 4, 0, 4),
                                    FontSize = 14
                                };

                                // если параграф явно отмечен как заголовок — повысим размер (простая эвристика)
                                var pStyle = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                                if (!string.IsNullOrEmpty(pStyle) && pStyle.ToLower().Contains("heading"))
                                {
                                    tb.FontSize = 18;
                                    tb.FontWeight = FontWeights.SemiBold;
                                    tb.Margin = new Thickness(0, 8, 0, 8);
                                }

                                stack.Children.Add(tb);
                            }
                        }
                        else if (element is Table tbl)
                        {
                            // Рендерим таблицу Word как WPF Grid
                            var tableGrid = RenderWordTable(tbl);
                            stack.Children.Add(tableGrid);
                        }
                        else
                        {
                            // другие элементы — игнорируем или обрабатываем при надобности
                        }
                    }
                }

                scroll.Content = stack;
                ClassButtonsPanel.Visibility = Visibility.Collapsed;
                ContentArea.Children.Clear();
                ContentArea.Children.Add(scroll);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия DOCX: {ex.Message}");
            }
        }

        private string GetParagraphText(Paragraph p)
        {
            // Собираем весь текст параграфа (включая runs)
            var runs = p.Descendants<Run>();
            var parts = runs.Select(r => r.GetFirstChild<Text>()?.Text).Where(t => t != null);
            return string.Join("", parts).Trim();
        }

        private Grid RenderWordTable(Table tbl)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 8, 0, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35))
            };

            // Определим количество столбцов — возьмём максимум по строкам
            int maxCols = tbl.Elements<TableRow>().Max(tr => tr.Elements<TableCell>().Count());

            for (int c = 0; c < maxCols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int rIndex = 0;
            foreach (var tr in tbl.Elements<TableRow>())
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                int cIndex = 0;
                foreach (var tc in tr.Elements<TableCell>())
                {
                    string cellText = string.Join("", tc.Descendants<Text>().Select(t => t.Text)).Trim();

                    var border = new System.Windows.Controls.Border
                    {
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6),
                        Margin = new Thickness(1),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 60))
                    };

                    var tb = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(cellText) ? " " : cellText,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    border.Child = tb;
                    Grid.SetRow(border, rIndex);
                    Grid.SetColumn(border, cIndex);
                    grid.Children.Add(border);

                    cIndex++;
                }

                // если в строке меньше столбцов — добавляем пустые ячейки
                while (cIndex < maxCols)
                {
                    var border = new System.Windows.Controls.Border
                    {
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6),
                        Margin = new Thickness(1),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35))
                    };

                    var tb = new TextBlock
                    {
                        Text = " ",
                        Foreground = Brushes.White
                    };

                    border.Child = tb;
                    Grid.SetRow(border, rIndex);
                    Grid.SetColumn(border, cIndex);
                    grid.Children.Add(border);

                    cIndex++;
                }

                rIndex++;
            }

            return grid;
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
