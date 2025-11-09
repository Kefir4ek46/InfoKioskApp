using ClosedXML.Excel;
using Microsoft.Web.WebView2.Wpf;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// явный алиас, чтобы избежать неоднозначности с OpenXml.TextAlignment
using SWTextAlignment = System.Windows.TextAlignment;

namespace InfoKioskApp.Views
{
    public partial class ScheduleView : UserControl
    {
        private string SchedulesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "schedules");

        // C# 7.3: явный конструктор, а не target-typed new()
        private Dictionary<string, string> _scheduleFiles = new Dictionary<string, string>();
        private Button _activeButton; // текущая активная кнопка (для подсветки)
        private Button _activeClassButton; // активная кнопка класса (листа Excel)



        public ScheduleView()
        {
            InitializeComponent();
            LoadSchedules();
        }

        private void LoadSchedules()
        {
            try
            {
                Directory.CreateDirectory(SchedulesRoot);
                string mainDir = Path.Combine(SchedulesRoot, "main");
                string changesDir = Path.Combine(SchedulesRoot, "changes");
                string otherDir = Path.Combine(SchedulesRoot, "other");

                Directory.CreateDirectory(mainDir);
                Directory.CreateDirectory(changesDir);
                Directory.CreateDirectory(otherDir);

                // === Получаем последние файлы ===
                string mainFile = Directory.GetFiles(mainDir)
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                string changesFile = Directory.GetFiles(changesDir)
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                var otherFiles = Directory.GetFiles(otherDir)
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToList();

                _scheduleFiles.Clear();
                _scheduleFiles["main"] = mainFile;
                _scheduleFiles["changes"] = changesFile;

                // === Создание кнопок ===
                ScheduleButtonsPanel.Children.Clear();

                var btnMain = CreateButton("📘 Основное", (s, e) =>
                {
                    HighlightActiveButton((Button)s);
                    ShowSchedule("main");
                });

                var btnChanges = CreateButton("📕 Изменённое", (s, e) =>
                {
                    HighlightActiveButton((Button)s);
                    ShowSchedule("changes");
                });


                ScheduleButtonsPanel.Children.Add(btnMain);
                ScheduleButtonsPanel.Children.Add(btnChanges);

                foreach (var file in otherFiles)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    var btn = CreateButton("📄 " + name, (s, e) =>
                    {
                        HighlightActiveButton((Button)s);
                        ShowScheduleFile(file);
                    });

                    ScheduleButtonsPanel.Children.Add(btn);
                }

                // Показать основное по умолчанию
                if (mainFile != null)
                    ShowSchedule("main");
                else if (changesFile != null)
                    ShowSchedule("changes");
                if (ScheduleButtonsPanel.Children.Count > 0)
                {
                    var firstBtn = ScheduleButtonsPanel.Children[0] as Button;
                    HighlightActiveButton(firstBtn);
                }

            }

            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки расписаний: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Button CreateButton(string text, RoutedEventHandler handler)
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
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += handler;
            return btn;
        }

        private void HighlightActiveButton(Button active)
        {
            foreach (Button b in ScheduleButtonsPanel.Children)
            {
                b.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(68, 68, 68));
                b.Foreground = Brushes.White;
            }

            if (active != null)
            {
                active.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 159, 255));
                active.Foreground = Brushes.White;
            }

            _activeButton = active;
        }
        private void HighlightActiveClassButton(Button active)
        {
            foreach (Button b in ClassButtonsPanel.Children)
            {
                b.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(68, 68, 68));
                b.Foreground = Brushes.White;
            }

            if (active != null)
            {
                active.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 159, 255));
                active.Foreground = Brushes.White;
            }

            _activeClassButton = active;
        }



        private void ShowSchedule(string key)
        {
            if (!_scheduleFiles.ContainsKey(key) || _scheduleFiles[key] == null)
            {
                MessageBox.Show("Файл расписания не найден.", "Инфо", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ShowScheduleFile(_scheduleFiles[key]);
        }

        private void ShowScheduleFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("Файл не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
                MessageBox.Show($"Неподдерживаемый формат: {ext}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        private int GetCurrentLessonIndex()
        {
            try
            {
                string bellFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "BellSchedule.json");
                if (!File.Exists(bellFile))
                    return -1;

                var json = File.ReadAllText(bellFile);
                var bells = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BellInfo>>(json);
                if (bells == null || bells.Count == 0)
                    return -1;

                DateTime now = DateTime.Now;
                for (int i = 0; i < bells.Count; i++)
                {
                    if (DateTime.TryParse(bells[i].Start, out DateTime start) &&
                        DateTime.TryParse(bells[i].End, out DateTime end))
                    {
                        if (now >= start && now <= end)
                            return i; // текущий урок
                    }
                }
            }
            catch { }
            return -1;
        }

        private class BellInfo
        {
            public string Start { get; set; }
            public string End { get; set; }
            public string Name { get; set; }
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
                        var btn = CreateButton(sheet, (s, e) =>
                        {
                            HighlightActiveClassButton((Button)s);
                            ShowExcelSheet(path, sheet);
                        });
                        ClassButtonsPanel.Children.Add(btn);
                    }


                    if (sheetNames.Any())
                    {
                        ShowExcelSheet(path, sheetNames[0]);

                        // Подсветка первой кнопки
                        var firstBtn = ClassButtonsPanel.Children.OfType<Button>().FirstOrDefault();
                        if (firstBtn != null)
                            HighlightActiveClassButton(firstBtn);
                    }

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения Excel: {ex.Message}");
            }
        }

        private void ShowExcelSheet(string path, string sheetName)
        {
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet(sheetName);
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                int currentLesson = -1;
                if (_scheduleFiles.ContainsKey("changes") && path == _scheduleFiles["changes"])
                    currentLesson = GetCurrentLessonIndex();

                var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

                // Если нет данных — показываем сообщение
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

                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
                };

                var grid = new Grid { Margin = new Thickness(10) };

                for (int c = 0; c < lastCol; c++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int r = 0; r < lastRow; r++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (int r = 1; r <= lastRow; r++)
                {
                    for (int c = 1; c <= lastCol; c++)
                    {
                        var cellText = ws.Cell(r, c).GetString();
                        bool isHeader = r == 1 || c == 1;

                        var border = new System.Windows.Controls.Border
                        {
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                            BorderThickness = new Thickness(1),
                            Background = isHeader
                                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 60))
                                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35)),
                            Padding = new Thickness(8),
                            Margin = new Thickness(2)
                        };

                        var text = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(cellText) ? " " : cellText,
                            Foreground = Brushes.White,
                            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                            TextAlignment = SWTextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        };

                        border.Child = text;
                        Grid.SetRow(border, r - 1);
                        Grid.SetColumn(border, c - 1);
                        if (r == currentLesson + 1 && !isHeader) // +1 потому что первая строка — заголовки
                        {
                            border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 100, 180));
                        }

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
            var viewer = new WebView2 { Source = new Uri(Path.GetFullPath(path)), Margin = new Thickness(5) };
            ClassButtonsPanel.Visibility = Visibility.Collapsed;
            ContentArea.Children.Clear();
            ContentArea.Children.Add(viewer);
        }
        #endregion

        #region === DOCX (Word) ===
        private void LoadDocxSchedule(string path)
        {
            try
            {
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

                                // если параграф явно отмечен как заголовок — выделяем
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
                            // ✅ Отрисовываем таблицу
                            var tableGrid = RenderWordTable(tbl);
                            stack.Children.Add(tableGrid);
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

        // === Чтение текста параграфа ===
        private string GetParagraphText(Paragraph p)
        {
            var runs = p.Descendants<Run>();
            var parts = runs.Select(r => r.GetFirstChild<Text>()?.Text).Where(t => t != null);
            return string.Join("", parts).Trim();
        }

        // === Преобразование таблицы Word в Grid ===
        private Grid RenderWordTable(Table tbl)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 8, 0, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35))
            };

            // Определяем количество столбцов — максимум по строкам
            int maxCols = tbl.Elements<TableRow>().Max(tr => tr.Elements<TableCell>().Count());

            for (int c = 0; c < maxCols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // === определяем номер текущего урока ===
            int currentLesson = GetCurrentLessonIndex();

            int rIndex = 0;
            foreach (var tr in tbl.Elements<TableRow>())
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int cIndex = 0;

                // Собираем текст всей строки
                string rowText = string.Join(" ", tr.Descendants<Text>().Select(t => t.Text)).Trim();

                // Если в строке упоминается "N урок", где N — текущий урок
                bool highlightRow = false;
                if (currentLesson > 0)
                {
                    string pattern1 = $"{currentLesson} урок";
                    string pattern2 = $"{currentLesson}-й урок";
                    if (rowText.Contains(pattern1) || rowText.Contains(pattern2))
                        highlightRow = true;
                }

                foreach (var tc in tr.Elements<TableCell>())
                {
                    string cellText = string.Join("", tc.Descendants<Text>().Select(t => t.Text)).Trim();

                    // цвет ячейки
                    var background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 60));
                    if (highlightRow)
                        background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 100, 180)); // подсветка активного урока

                    var border = new System.Windows.Controls.Border
                    {
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6),
                        Margin = new Thickness(1),
                        Background = background
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


        #region === Image ===
        private void LoadImageSchedule(string path)
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(Path.GetFullPath(path))),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ClassButtonsPanel.Visibility = Visibility.Collapsed;
            ContentArea.Children.Clear();
            ContentArea.Children.Add(img);
        }
        #endregion
    }
}

