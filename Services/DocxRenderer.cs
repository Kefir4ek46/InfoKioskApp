using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Border = System.Windows.Controls.Border;
using Color = System.Windows.Media.Color;
using TextAlignment = System.Windows.TextAlignment;

namespace InfoKioskApp.Services
{
    public class DocxRenderer
    {
        private readonly Brush _nextLessonBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e6c229")); // жёлтый
        private readonly Dictionary<Border, Storyboard> _pulseAnimations = [];

        private readonly DispatcherTimer _highlightTimer;
        private Grid _lastGrid;
        private readonly Dictionary<Border, Brush> _baseBackground = [];

        // highlight color (green)
        private readonly Brush _highlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4caf50"));

        public DocxRenderer()
        {
            _highlightTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _highlightTimer.Tick += (s, e) => UpdateHighlight();
        }

        private static Storyboard CreatePulseAnimation(Border border)
        {
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.4,
                Duration = TimeSpan.FromSeconds(0.8),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            var sb = new Storyboard();
            sb.Children.Add(anim);

            Storyboard.SetTarget(anim, border);
            Storyboard.SetTargetProperty(anim, new PropertyPath(Border.OpacityProperty));

            return sb;
        }

        private void StopPulse(Border b)
        {
            if (b == null) return;

            if (_pulseAnimations.TryGetValue(b, out Storyboard sb))
            {
                try { sb.Stop(); } catch { }
                b.Opacity = 1.0;
                _pulseAnimations.Remove(b);
            }
        }

        private void StopAllPulses()
        {
            foreach (var kv in _pulseAnimations.ToList())
            {
                try { kv.Value.Stop(); } catch { }
                try { kv.Key.Opacity = 1.0; } catch { }
            }
            _pulseAnimations.Clear();
        }

        // PUBLIC API: возвращает StackPanel с содержимым docx (параграфы и таблицы)
        public UIElement LoadDocx(string path)
        {
            // при загрузке нового документа — остановим все старые анимации и снимем таймер (если нужно)
            StopAllPulses();
            _baseBackground.Clear();
            _lastGrid = null;

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
                    stack.Children.Add(new TextBlock
                    {
                        Text = "Документ пуст.",
                        Foreground = Brushes.Gray,
                        FontSize = 16,
                        TextAlignment = TextAlignment.Center
                    });
                    return stack;
                }

                foreach (var el in body.Elements())
                {
                    if (el is Paragraph p)
                    {
                        var tb = RenderParagraph(p);
                        if (tb != null) stack.Children.Add(tb);
                    }
                    else if (el is Table t)
                    {
                        var grid = RenderTable(t);
                        if (grid != null)
                        {
                            stack.Children.Add(grid);
                            _lastGrid = grid;
                        }
                    }
                }
            }

            // стартуем таймер подсветки (если необходимо)
            UpdateHighlight();
            if (_lastGrid != null && !_highlightTimer.IsEnabled) _highlightTimer.Start();

            return stack;
        }

        // Render paragraph with style/align
        private static TextBlock RenderParagraph(Paragraph p)
        {
            string text = string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();
            if (string.IsNullOrEmpty(text)) return null;

            string style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
            var justify = p.ParagraphProperties?.Justification?.Val;

            TextAlignment align = TextAlignment.Left;
            if (justify != null)
            {
                var a = justify.Value.ToString().ToLower();
                if (a == "center") align = TextAlignment.Center;
                else if (a == "right") align = TextAlignment.Right;
            }

            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = style.Contains("heading", StringComparison.CurrentCultureIgnoreCase) ? 20 : 16,
                FontWeight = style.Contains("heading", StringComparison.CurrentCultureIgnoreCase) ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 6, 0, 6),
                TextAlignment = align
            };
        }

        // Internal structures
        private enum VMergeKind { None, Restart, Continue }

        private class CellData
        {
            public string Text { get; set; }
            public int ColSpan { get; set; }
            public VMergeKind VMerge { get; set; }

            public CellData() { Text = string.Empty; ColSpan = 1; VMerge = VMergeKind.None; }
            public CellData(string text, int colspan, VMergeKind vmerge)
            {
                Text = text ?? string.Empty;
                ColSpan = Math.Max(1, colspan);
                VMerge = vmerge;
            }
        }

        // Core table renderer — "Word-like" grid filling
        private Grid RenderTable(Table t)
        {
            var wordRows = t.Elements<TableRow>().ToList();
            if (wordRows.Count == 0) return null;

            // Try to read tblGrid for authoritative column count
            int gridColsFromTblGrid = -1;
            var tblGrid = t.Elements<TableGrid>().FirstOrDefault();
            if (tblGrid != null)
            {
                var cols = tblGrid.Elements<GridColumn>().ToList();
                if (cols != null && cols.Count > 0) gridColsFromTblGrid = cols.Count;
            }

            // Parse raw cells per row (respecting gridSpan and vMerge)
            var parsedRows = new List<List<CellData>>();
            for (int rowIndex = 0; rowIndex < wordRows.Count; rowIndex++)
            {
                var row = wordRows[rowIndex];
                var list = new List<CellData>();

                foreach (var cell in row.Elements<TableCell>())
                {
                    string txt = string.Join(" ", cell.Descendants<Text>().Select(x => x.Text)).Trim();
                    txt ??= string.Empty;

                    int colspan = 1;
                    VMergeKind vmerge = VMergeKind.None;

                    var props = cell.TableCellProperties;
                    if (props != null)
                    {
                        var gridSpan = props.GetFirstChild<GridSpan>();
                        if (gridSpan?.Val != null && gridSpan.Val.HasValue)
                        {
                            try { colspan = (int)gridSpan.Val.Value; } catch { colspan = 1; }
                        }

                        var vm = props.GetFirstChild<VerticalMerge>();
                        if (vm != null)
                        {
                            if (vm.Val == null) vmerge = VMergeKind.Continue; // <w:vMerge/>
                            else if (vm.Val.HasValue && vm.Val.Value == MergedCellValues.Restart) vmerge = VMergeKind.Restart;
                            else if (vm.Val.HasValue && vm.Val.Value == MergedCellValues.Continue) vmerge = VMergeKind.Continue;
                        }
                    }

                    list.Add(new CellData(txt, Math.Max(1, colspan), vmerge));
                }

                parsedRows.Add(list);
            }

            // Determine maxCols: prefer tblGrid if present, else compute by summing colspans
            int maxCols = gridColsFromTblGrid > 0 ? gridColsFromTblGrid : parsedRows.Max(r =>
            {
                static int selector(CellData c) => Math.Max(1, c.ColSpan);
                return r.Sum(selector);
            });
            int maxRows = parsedRows.Count;

            // APPLY gridBefore/gridAfter adjustments per row if present
            for (int r = 0; r < wordRows.Count; r++)
            {
                int before = 0;
                int after = 0;
                var trPr = wordRows[r].TableRowProperties;
                if (trPr != null)
                {
                    var gb = trPr.GetFirstChild<GridBefore>();
                    var ga = trPr.GetFirstChild<GridAfter>();
                    if (gb?.Val?.HasValue == true) before = (int)gb.Val.Value;
                    if (ga?.Val?.HasValue == true) after = (int)ga.Val.Value;
                }

                if (before > 0)
                {
                    var empty = new List<CellData>();
                    for (int i = 0; i < before; i++)
                        empty.Add(new CellData(string.Empty, 1, VMergeKind.None));
                    parsedRows[r].InsertRange(0, empty);
                }

                // ensure row has maxCols by adding empty slots at end
                int total = parsedRows[r].Sum(c => c.ColSpan);
                if (total < maxCols)
                {
                    int diff = maxCols - total;
                    for (int i = 0; i < diff; i++)
                        parsedRows[r].Add(new CellData(string.Empty, 1, VMergeKind.None));
                }
            }

            // Normalize rows into logical slots: each logical slot = one column
            var normalized = new List<List<CellData>>();
            foreach (var r in parsedRows)
            {
                var nr = new List<CellData>();
                foreach (var c in r)
                {
                    // push the original cell as the starting slot
                    nr.Add(new CellData(c.Text, c.ColSpan, c.VMerge));
                    // add placeholder slots for the remaining columns of colspan
                    for (int i = 1; i < c.ColSpan; i++)
                        nr.Add(new CellData(string.Empty, 1, VMergeKind.None)); // logical placeholder
                }

                // pad or trim to maxCols
                while (nr.Count < maxCols) nr.Add(new CellData(string.Empty, 1, VMergeKind.None));
                if (nr.Count > maxCols) nr = [.. nr.Take(maxCols)];

                normalized.Add(nr);
            }

            // Create Grid
            var grid = new Grid
            {
                Margin = new Thickness(0, 10, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(35, 35, 35))
            };
            for (int i = 0; i < maxCols; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < maxRows; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Occupied matrix to avoid double placement
            bool[,] occupied = new bool[maxRows, maxCols];

            // Place cells: iterate rows and logical columns
            for (int r = 0; r < maxRows; r++)
            {
                for (int col = 0; col < maxCols; col++)
                {
                    if (occupied[r, col]) continue;

                    // get corresponding normalized slot
                    CellData slot = null;
                    if (col < normalized[r].Count) slot = normalized[r][col];
                    if (slot == null)
                    {
                        occupied[r, col] = true;
                        continue;
                    }

                    // If slot is a continuation placeholder of vertical merge — skip drawing (drawn by top cell)
                    if (slot.VMerge == VMergeKind.Continue)
                    {
                        occupied[r, col] = true;
                        continue;
                    }

                    int colspan = Math.Max(1, slot.ColSpan);

                    // Determine rowspan if slot.VMerge==Restart
                    int rowspan = 1;
                    if (slot.VMerge == VMergeKind.Restart)
                    {
                        for (int rr = r + 1; rr < maxRows; rr++)
                        {
                            if (col < normalized[rr].Count)
                            {
                                var below = normalized[rr][col];
                                if (below.VMerge == VMergeKind.Continue)
                                {
                                    rowspan++;
                                    continue;
                                }
                            }
                            break;
                        }
                    }

                    // Mark occupied area
                    for (int rr = r; rr < r + rowspan && rr < maxRows; rr++)
                        for (int cc = col; cc < col + colspan && cc < maxCols; cc++)
                            occupied[rr, cc] = true;

                    // Render cell (we render visible borders even for empty slots so grid is complete)
                    {
                        bool isHeader = (r == 0);
                        bool isFirstCol = (col == 0);
                        bool highlightRow = (!isHeader && (r - 1 == GetCurrentLessonIndex()));

                        Brush baseBg = new SolidColorBrush(Color.FromRgb(45, 45, 60));
                        if (isHeader) baseBg = new SolidColorBrush(Color.FromRgb(60, 60, 90));
                        else if (isFirstCol) baseBg = new SolidColorBrush(Color.FromRgb(55, 55, 75));
                        if (highlightRow) baseBg = _highlightBrush;

                        var border = new Border
                        {
                            Background = baseBg,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(8),
                            Margin = new Thickness(0.5)
                        };

                        var tb = new TextBlock
                        {
                            Text = slot.Text,
                            Foreground = Brushes.White,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 15,
                            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal
                        };

                        // rotate text vertically for large rowspan in first column
                        if (isFirstCol && rowspan > 1)
                        {
                            tb.LayoutTransform = new RotateTransform(-90);
                            tb.Margin = new Thickness(0);
                            tb.TextAlignment = TextAlignment.Center;
                        }

                        border.Child = tb;
                        _baseBackground[border] = baseBg;

                        Grid.SetRow(border, r);
                        Grid.SetColumn(border, col);
                        if (colspan > 1) Grid.SetColumnSpan(border, colspan);
                        if (rowspan > 1) Grid.SetRowSpan(border, rowspan);

                        grid.Children.Add(border);
                    }
                }
            }

            return grid;
        }

        private static int GetNextLessonIndex(int current)
        {
            try
            {
                string bellPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "BellSchedule.json");
                if (!System.IO.File.Exists(bellPath)) return -1;

                var json = System.IO.File.ReadAllText(bellPath);
                var bells = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BellEntry>>(json);
                if (bells == null || bells.Count == 0) return -1;

                if (current >= 0 && current < bells.Count - 1)
                    return current + 1;

                if (current < 0)
                {
                    DateTime now = DateTime.Now;

                    for (int i = 0; i < bells.Count; i++)
                    {
                        if (DateTime.TryParse(bells[i].Start, out DateTime s))
                        {
                            if (now < DateTime.Today.Add(s.TimeOfDay))
                                return i;
                        }
                    }
                }
            }
            catch { }

            return -1;
        }

        // Get current lesson index from BellSchedule.json; returns -1 if none
        private static int GetCurrentLessonIndex()
        {
            try
            {
                string bellPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "BellSchedule.json");
                if (!System.IO.File.Exists(bellPath)) return -1;

                var json = System.IO.File.ReadAllText(bellPath);
                var bells = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BellEntry>>(json);
                if (bells == null || bells.Count == 0) return -1;

                DateTime now = DateTime.Now;
                for (int i = 0; i < bells.Count; i++)
                {
                    if (DateTime.TryParse(bells[i].Start, out DateTime s) && DateTime.TryParse(bells[i].End, out DateTime e))
                    {
                        var start = DateTime.Today.Add(s.TimeOfDay);
                        var end = DateTime.Today.Add(e.TimeOfDay);
                        if (now >= start && now <= end) return i;
                    }
                }
            }
            catch
            {
                // ignore parsing errors
            }
            return -1;
        }

        // Update highlight for current lesson (safe: checks current >= 0)
        private void UpdateHighlight()
        {
            if (_lastGrid == null) return;

            int current = GetCurrentLessonIndex();         // индекс урока 0-based или -1
            int next = GetNextLessonIndex(current);        // индекс следующего урока 0-based или -1

            // Найдём физические строки в Grid, соответствующие номеру урока (в колонке №)
            int currentRow = (current >= 0) ? FindRowByLessonNumber(current + 1) : -1;
            int nextRow = (next >= 0) ? FindRowByLessonNumber(next + 1) : -1;

            // Если начался урок (current >=0) — убедимся, что старые пульсации сняты (например, предыдущие анимации)
            if (current >= 0)
            {
                // остановим любые пульсации, которые остались от предыдущих состояний
                StopAllPulses();
            }

            // Если сейчас перемена — хотим показать следующий урок (пульсация) и оставить прошлый зелёным
            // Если сейчас урок — только текущий зелёный, пульсаций нет
            foreach (UIElement elem in _lastGrid.Children)
            {
                if (elem is not Border b) continue;

                int startRow = Grid.GetRow(b);
                int rowspan = Grid.GetRowSpan(b);
                if (rowspan < 1) rowspan = 1;
                int endRow = startRow + rowspan - 1;

                bool coversCurrent = (currentRow >= 0 && currentRow >= startRow && currentRow <= endRow);
                bool coversNextDuringBreak = (nextRow >= 0 && current < 0 && nextRow >= startRow && nextRow <= endRow);

                // Если ячейка покрывает текущий урок — зелёная
                if (coversCurrent)
                {
                    b.Background = _highlightBrush;
                    StopPulse(b);
                    continue;
                }

                // Если ячейка покрывает следующий урок и сейчас перемена — жёлтая с пульсацией
                if (coversNextDuringBreak)
                {
                    b.Background = _nextLessonBrush;

                    if (!_pulseAnimations.ContainsKey(b))
                    {
                        Storyboard sb = CreatePulseAnimation(b);
                        _pulseAnimations[b] = sb;
                        sb.Begin();
                    }
                    continue;
                }

                // Иначе — вернуть базовый фон (и остановить пульсацию, если была)
                StopPulse(b);

                if (_baseBackground.TryGetValue(b, out Brush baseBg))
                    b.Background = baseBg;
                else
                    b.Background = new SolidColorBrush(Color.FromRgb(45, 45, 60));
            }
        }

        // Находим строку Grid, где в любой колонке находится ячейка с числом "lesson"
        // (убрал привязку к col==0, потому что номер может быть в другой логической колонке)
        private int FindRowByLessonNumber(int lesson)
        {
            if (_lastGrid == null) return -1;

            foreach (UIElement elem in _lastGrid.Children)
            {
                if (elem is not Border b) continue;

                if (b.Child is not TextBlock tb) continue;

                if (int.TryParse(tb.Text?.Trim(), out int num))
                {
                    if (num == lesson)
                        return Grid.GetRow(b);
                }
            }
            return -1;
        }

        private class BellEntry
        {
            public string Start { get; set; }
            public string End { get; set; }
            public string Name { get; set; }
        }
    }
}
