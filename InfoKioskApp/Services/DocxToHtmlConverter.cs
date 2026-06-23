using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace InfoKioskApp.Services
{
    /// <summary>
    /// Конвертирует DOCX в HTML-таблицу для отображения в WebView2 киоска.
    /// Поддерживает объединённые ячейки (gridSpan, vMerge).
    /// Возвращает JSON: { html: "...", hasTable: true/false }
    /// </summary>
    public static class DocxToHtmlConverter
    {
        private enum VMergeKind { None, Restart, Continue }

        private class CellData
        {
            public string Text { get; set; } = "";
            public int ColSpan { get; set; } = 1;
            public VMergeKind VMerge { get; set; } = VMergeKind.None;
        }

        /// <summary>
        /// Конвертирует DOCX в HTML. Возвращает JSON-объект.
        /// </summary>
        public static object Convert(string docxPath)
        {
            try
            {
                if (!File.Exists(docxPath))
                    return new { html = "", hasTable = false, error = "Файл не найден" };

                var sb = new StringBuilder();
                bool hasTable = false;

                using (var doc = WordprocessingDocument.Open(docxPath, false))
                {
                    var body = doc.MainDocumentPart?.Document?.Body;
                    if (body == null)
                        return new { html = "<p>Документ пуст.</p>", hasTable = false };

                    foreach (var el in body.Elements())
                    {
                        if (el is Paragraph p)
                        {
                            string text = string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();
                            if (!string.IsNullOrEmpty(text))
                            {
                                string style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                                bool isHeading = style.Contains("heading", StringComparison.OrdinalIgnoreCase);
                                var justify = p.ParagraphProperties?.Justification?.Val;
                                string align = "left";
                                if (justify != null)
                                {
                                    var a = justify.Value.ToString().ToLower();
                                    if (a == "center") align = "center";
                                    else if (a == "right") align = "right";
                                }
                                sb.Append($"<p style=\"text-align:{align};font-size:{(isHeading ? "20" : "16")}px;font-weight:{(isHeading ? "600" : "400")};margin:6px 0;color:#fff;\">");
                                sb.Append(EscapeHtml(text));
                                sb.Append("</p>");
                            }
                        }
                        else if (el is Table t)
                        {
                            hasTable = true;
                            sb.Append(ConvertTable(t));
                        }
                    }
                }

                return new { html = sb.ToString(), hasTable };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DocxToHtml] Error: {ex.Message}");
                return new { html = "", hasTable = false, error = ex.Message };
            }
        }

        private static string ConvertTable(Table t)
        {
            var wordRows = t.Elements<TableRow>().ToList();
            if (wordRows.Count == 0) return "";

            // Read tblGrid for column count
            int gridCols = -1;
            var tblGrid = t.Elements<TableGrid>().FirstOrDefault();
            if (tblGrid != null)
            {
                var cols = tblGrid.Elements<GridColumn>().ToList();
                if (cols.Count > 0) gridCols = cols.Count;
            }

            // Parse rows
            var parsedRows = new List<List<CellData>>();
            for (int rowIndex = 0; rowIndex < wordRows.Count; rowIndex++)
            {
                var row = wordRows[rowIndex];
                var list = new List<CellData>();

                foreach (var cell in row.Elements<TableCell>())
                {
                    string txt = string.Join(" ", cell.Descendants<Text>().Select(x => x.Text)).Trim();
                    txt ??= "";
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
                            if (vm.Val == null) vmerge = VMergeKind.Continue;
                            else if (vm.Val.HasValue && vm.Val.Value == MergedCellValues.Restart) vmerge = VMergeKind.Restart;
                            else if (vm.Val.HasValue && vm.Val.Value == MergedCellValues.Continue) vmerge = VMergeKind.Continue;
                        }
                    }
                    list.Add(new CellData { Text = txt, ColSpan = Math.Max(1, colspan), VMerge = vmerge });
                }
                parsedRows.Add(list);
            }

            // Determine maxCols
            int maxCols = gridCols > 0 ? gridCols : parsedRows.Max(r => r.Sum(c => Math.Max(1, c.ColSpan)));
            int maxRows = parsedRows.Count;

            // Handle gridBefore/gridAfter
            for (int r = 0; r < wordRows.Count; r++)
            {
                int before = 0;
                var trPr = wordRows[r].TableRowProperties;
                if (trPr != null)
                {
                    var gb = trPr.GetFirstChild<GridBefore>();
                    if (gb?.Val?.HasValue == true) before = (int)gb.Val.Value;
                }
                if (before > 0)
                {
                    var empty = new List<CellData>();
                    for (int i = 0; i < before; i++)
                        empty.Add(new CellData());
                    parsedRows[r].InsertRange(0, empty);
                }
                int total = parsedRows[r].Sum(c => c.ColSpan);
                if (total < maxCols)
                {
                    int diff = maxCols - total;
                    for (int i = 0; i < diff; i++)
                        parsedRows[r].Add(new CellData());
                }
            }

            // Normalize into logical slots
            var normalized = new List<List<CellData>>();
            foreach (var r in parsedRows)
            {
                var nr = new List<CellData>();
                foreach (var c in r)
                {
                    nr.Add(new CellData { Text = c.Text, ColSpan = c.ColSpan, VMerge = c.VMerge });
                    for (int i = 1; i < c.ColSpan; i++)
                        nr.Add(new CellData());
                }
                while (nr.Count < maxCols) nr.Add(new CellData());
                if (nr.Count > maxCols) nr = nr.Take(maxCols).ToList();
                normalized.Add(nr);
            }

            // Build HTML table
            var sb = new StringBuilder();
            sb.Append("<table class=\"schedule-table docx-table\">");

            // Occupied matrix
            var occupied = new bool[maxRows, maxCols];

            for (int r = 0; r < maxRows; r++)
            {
                // Определяем класс для строки (для подсветки).
                string trClass = "";
                if (r > 0) // не заголовок
                {
                    trClass = $" lesson-row-{r - 1}"; // 0-based для строк данных
                }
                sb.Append($"<tr class=\"{trClass.Trim()}\">");
                for (int col = 0; col < maxCols; col++)
                {
                    if (occupied[r, col]) continue;

                    CellData slot = null;
                    if (col < normalized[r].Count) slot = normalized[r][col];
                    if (slot == null) { occupied[r, col] = true; continue; }

                    if (slot.VMerge == VMergeKind.Continue)
                    {
                        occupied[r, col] = true;
                        continue;
                    }

                    int colspan = Math.Max(1, slot.ColSpan);
                    int rowspan = 1;
                    if (slot.VMerge == VMergeKind.Restart)
                    {
                        for (int rr = r + 1; rr < maxRows; rr++)
                        {
                            if (col < normalized[rr].Count && normalized[rr][col].VMerge == VMergeKind.Continue)
                                rowspan++;
                            else break;
                        }
                    }

                    for (int rr = r; rr < r + rowspan && rr < maxRows; rr++)
                        for (int cc = col; cc < col + colspan && cc < maxCols; cc++)
                            occupied[rr, cc] = true;

                    bool isHeader = (r == 0);
                    bool isFirstCol = (col == 0);
                    int lessonNum = r; // 0-based, will be r for data rows

                    string cls = "schedule-cell";
                    if (isHeader) cls += " schedule-header";
                    if (isFirstCol) cls += " schedule-first-col";

                    string colspanAttr = colspan > 1 ? $" colspan=\"{colspan}\"" : "";
                    string rowspanAttr = rowspan > 1 ? $" rowspan=\"{rowspan}\"" : "";
                    string styleAttr = "";
                    if (isFirstCol && rowspan > 1)
                        styleAttr = " style=\"writing-mode:vertical-lr;transform:rotate(180deg);\"";

                    sb.Append($"<td class=\"{cls}\"{colspanAttr}{rowspanAttr}{styleAttr}>");
                    sb.Append(EscapeHtml(slot.Text));
                    sb.Append("</td>");
                }
                sb.Append("</tr>");
            }

            sb.Append("</table>");
            return sb.ToString();
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\"", "&quot;").Replace("'", "&#39;");
        }
    }
}
