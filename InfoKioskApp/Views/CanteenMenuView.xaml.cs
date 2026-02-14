using ClosedXML.Excel;
using System;
using System.Data;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfoKioskApp.Views
{
    public partial class CanteenMenuView : UserControl
    {
        private readonly string BaseUrl = "https://foodmonitoring.ru";
        private readonly string FoodBlockId = "15159";

        private readonly string MenuFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "CanteenMenu");

        public CanteenMenuView()
        {
            InitializeComponent();

            Directory.CreateDirectory(MenuFolder);

            Loaded += CanteenMenuView_Loaded;
        }

        #region Загрузка вкладки

        private async void CanteenMenuView_Loaded(object sender, RoutedEventArgs e)
        {
            await PreloadMenus();
            await SelectTodayAndLoad();
        }

        #endregion

        #region Предзагрузка меню

        private async Task PreloadMenus()
        {
            for (int i = 0; i <= 4; i++)
            {
                var date = DateTime.Today.AddDays(i);
                await EnsureMenuFileExists(date);
            }
        }

        #endregion

        #region Проверка и скачивание файла

        private async Task<string?> EnsureMenuFileExists(DateTime date)
        {
            string fileName = $"{date:yyyy-MM-dd}-sm.xlsx";
            string localPath = Path.Combine(MenuFolder, fileName);

            if (File.Exists(localPath))
                return localPath;

            string url = $"{BaseUrl}/{FoodBlockId}/food/{fileName}";

            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var bytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(localPath, bytes);

                return localPath;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Работа с датами

        private DateTime GetDateForDay(DayOfWeek targetDay)
        {
            var today = DateTime.Today;
            int diff = targetDay - today.DayOfWeek;
            return today.AddDays(diff);
        }

        private async Task SelectTodayAndLoad()
        {
            var today = DateTime.Today.DayOfWeek;

            foreach (Button btn in DaysPanel.Children)
            {
                if (btn.Tag.ToString() == today.ToString())
                {
                    SetActiveButton(btn);
                    await LoadMenuForDate(DateTime.Today);
                    return;
                }
            }

            StatusText.Text = "Сегодня меню отсутствует";
        }

        #endregion

        #region Кнопки дней

        private async void DayButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var day = Enum.Parse<DayOfWeek>(button.Tag.ToString());

            SetActiveButton(button);

            var date = GetDateForDay(day);
            await LoadMenuForDate(date);
        }

        private void SetActiveButton(Button activeButton)
        {
            var normalBackground = new SolidColorBrush(Color.FromRgb(58, 58, 58));   // #3A3A3A
            var activeBackground = new SolidColorBrush(Color.FromRgb(45, 140, 255)); // синий

            foreach (Button btn in DaysPanel.Children)
            {
                btn.Background = normalBackground;
                btn.Foreground = Brushes.White;
            }

            activeButton.Background = activeBackground;
            activeButton.Foreground = Brushes.White;
        }


        #endregion

        #region Загрузка меню в DataGrid

        private async Task LoadMenuForDate(DateTime date)
        {
            StatusText.Text = "Загрузка...";
            MenuGrid.ItemsSource = null;

            var filePath = await EnsureMenuFileExists(date);

            if (filePath == null)
            {
                StatusText.Text = "На этот день меню не загружено";
                return;
            }

            var table = LoadExcel(filePath);
            MenuGrid.ItemsSource = table.DefaultView;
            StatusText.Text = "";
        }

        #endregion

        #region Парсинг Excel

        private DataTable LoadExcel(string path)
        {
            var table = new DataTable();

            using var workbook = new XLWorkbook(path);
            var worksheet = workbook.Worksheet(1);

            bool firstRow = true;

            foreach (var row in worksheet.RowsUsed())
            {
                if (firstRow)
                {
                    foreach (var cell in row.Cells())
                        table.Columns.Add(cell.Value.ToString());

                    firstRow = false;
                }
                else
                {
                    var dataRow = table.NewRow();
                    int i = 0;

                    foreach (var cell in row.Cells(1, table.Columns.Count))
                    {
                        dataRow[i++] = cell.Value.ToString();
                    }

                    table.Rows.Add(dataRow);
                }
            }

            return table;
        }

        #endregion
    }
}
