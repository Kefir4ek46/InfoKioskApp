using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using InfoKioskApp.Services;
using Microsoft.Web.WebView2.Wpf;
using OpenXmlPowerTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Border = System.Windows.Controls.Border;
using Color = System.Windows.Media.Color;
// явный алиас, чтобы избежать неоднозначности с OpenXml.TextAlignment
using SWTextAlignment = System.Windows.TextAlignment;
using TextAlignment = System.Windows.TextAlignment;




namespace InfoKioskApp.Views
{
    public partial class ScheduleView : UserControl
    {
        private string SchedulesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "schedules");

        // C# 7.3: явный конструктор, а не target-typed new()
        private Dictionary<string, string> _scheduleFiles = new Dictionary<string, string>();
        private Button _activeButton; // текущая активная кнопка (для подсветки)
        private Button _activeClassButton; // активная кнопка класса (листа Excel)

        private List<List<Border>> _tableRowBorders = new List<List<Border>>();
        


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
            // 🧹 Всегда очищаем кнопки листов при загрузке нового файла
            ClassButtonsPanel.Children.Clear();
            ClassButtonsPanel.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("Файл не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string ext = Path.GetExtension(path).ToLower();

            if (ext == ".xlsx")
            {
                ClassButtonsPanel.Visibility = Visibility.Visible;
                LoadExcelSchedule(path);
            }
            else if (ext == ".pdf")
                LoadPdfSchedule(path);
            else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                LoadImageSchedule(path);
            else if (ext == ".docx")
                LoadDocxSchedule(path);
            else
                MessageBox.Show($"Неподдерживаемый формат: {ext}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

                if (lastRow == 0 || lastCol == 0)
                {
                    ContentArea.Children.Clear();
                    ContentArea.Children.Add(new TextBlock
                    {
                        Text = "Нет данных",
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
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
                };

                var grid = new Grid { Margin = new Thickness(3) };

                // Создание столбцов
                for (int c = 0; c < lastCol; c++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Создание строк
                for (int r = 0; r < lastRow; r++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Объединённые ячейки
                var merged = new List<Tuple<int, int, int, int>>();
                foreach (var m in ws.MergedRanges)
                {
                    merged.Add(Tuple.Create(
                        m.FirstRow().RowNumber(),
                        m.FirstColumn().ColumnNumber(),
                        m.RowCount(),
                        m.ColumnCount()
                    ));
                }

                bool[,] occupied = new bool[lastRow + 1, lastCol + 1];

                // Рендер таблицы
                for (int r = 1; r <= lastRow; r++)
                {
                    for (int c = 1; c <= lastCol; c++)
                    {
                        if (occupied[r, c]) continue;

                        Tuple<int, int, int, int> merge = null;

                        foreach (var m in merged)
                        {
                            if (m.Item1 == r && m.Item2 == c)
                            {
                                merge = m;
                                break;
                            }
                        }

                        int rowspan = merge != null ? merge.Item3 : 1;
                        int colspan = merge != null ? merge.Item4 : 1;

                        if (merge != null)
                        {
                            int maxR = lastRow;
                            int maxC = lastCol;

                            int endR = Math.Min(r + rowspan - 1, maxR);
                            int endC = Math.Min(c + colspan - 1, maxC);

                            for (int rr = r; rr <= endR; rr++)
                            {
                                for (int cc = c; cc <= endC; cc++)
                                {
                                    occupied[rr, cc] = true;
                                }
                            }

                        }
                        else
                        {
                            occupied[r, c] = true;
                        }

                        string text = ws.Cell(r, c).GetString();

                        var border = new Border
                        {
                            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                            BorderThickness = new Thickness(1),
                            Background = (r == 1)
                                ? new SolidColorBrush(Color.FromRgb(45, 45, 60))
                                : new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                            Padding = new Thickness(8),
                            Margin = new Thickness(1)
                        };

                        var tb = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(text) ? " " : text,
                            Foreground = Brushes.White,
                            FontWeight = r == 1 ? FontWeights.SemiBold : FontWeights.Normal,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        border.Child = tb;

                        Grid.SetRow(border, r - 1);
                        Grid.SetColumn(border, c - 1);
                        if (rowspan > 1) Grid.SetRowSpan(border, rowspan);
                        if (colspan > 1) Grid.SetColumnSpan(border, colspan);

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
            var renderer = new DocxRenderer();
            ContentArea.Children.Clear();
            ContentArea.Children.Add(renderer.LoadDocx(path));

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

