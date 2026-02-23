using ClosedXML.Excel;
using InfoKioskApp.Services;
using System;
using System.Data;
using System.IO;
using System.Linq;
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

        public CanteenMenuView()
        {
            InitializeComponent();

            Loaded += CanteenMenuView_Loaded;
        }

        private string GetFoodBlockId()
        {
            var configuredValue = ConfigService.LoadConfig().FoodBlockId?.Trim();
            return string.IsNullOrWhiteSpace(configuredValue) ? "15159" : configuredValue;
        }

        private string GetMenuFolderForFoodBlock(string foodBlockId)
        {
            var safeFoodBlockId = string.Join("_", foodBlockId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var menuFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "CanteenMenu", safeFoodBlockId);
            Directory.CreateDirectory(menuFolder);
            return menuFolder;
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
            string foodBlockId = GetFoodBlockId();
            string menuFolder = GetMenuFolderForFoodBlock(foodBlockId);
            string fileName = $"{date:yyyy-MM-dd}-sm.xlsx";
            string localPath = Path.Combine(menuFolder, fileName);

            if (File.Exists(localPath))
                return localPath;

            string url = $"{BaseUrl}/{foodBlockId}/food/{fileName}";

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

            BreakfastPanel.Children.Clear();
            Breakfast2Panel.Children.Clear();
            LunchPanel.Children.Clear();
            SetSectionsVisibility(false, false, false);

            DateText.Text = $"Меню на {date:dd.MM.yyyy}";

            var filePath = await EnsureMenuFileExists(date);

            if (filePath == null)
            {
                StatusText.Text = "На этот день меню не загружено";
                return;
            }

            
            ParseAndRenderExcel(filePath);

            StatusText.Text = "";

        }


        #endregion

        private void SetSectionsVisibility(bool breakfastVisible, bool breakfast2Visible, bool lunchVisible)
        {
            BreakfastHeader.Visibility = breakfastVisible ? Visibility.Visible : Visibility.Collapsed;
            BreakfastPanel.Visibility = breakfastVisible ? Visibility.Visible : Visibility.Collapsed;

            Breakfast2Header.Visibility = breakfast2Visible ? Visibility.Visible : Visibility.Collapsed;
            Breakfast2Panel.Visibility = breakfast2Visible ? Visibility.Visible : Visibility.Collapsed;

            LunchHeader.Visibility = lunchVisible ? Visibility.Visible : Visibility.Collapsed;
            LunchPanel.Visibility = lunchVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        #region Парсинг Excel

        private void ParseAndRenderExcel(string path)
        {
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(1);
            bool breakfastHeaderAdded = false;
            bool breakfast2HeaderAdded = false;
            bool lunchHeaderAdded = false;


            string currentMeal = "";

            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                var mealCell = row.Cell(1).GetString().Trim();

                if (!string.IsNullOrWhiteSpace(mealCell))
                    currentMeal = mealCell;

                var section = row.Cell(2).GetString();
                var recipe = row.Cell(3).GetString();
                var dish = row.Cell(4).GetString();
                var output = row.Cell(5).GetString();
                var price = row.Cell(6).GetString();
                var calories = row.Cell(7).GetString();
                var proteins = row.Cell(8).GetString();
                var fats = row.Cell(9).GetString();
                var carbs = row.Cell(10).GetString();

                if (string.IsNullOrWhiteSpace(dish))
                    continue;

                var rowGrid = CreateMenuRow(section, recipe, dish, output, price, calories, proteins, fats, carbs);

                switch (currentMeal.ToLower())
                {
                    case "завтрак":
                        if (!breakfastHeaderAdded)
                        {
                            BreakfastPanel.Children.Add(CreateHeaderRow());
                            breakfastHeaderAdded = true;
                        }
                        BreakfastPanel.Children.Add(rowGrid);
                        break;

                    case "завтрак 2":
                        if (!breakfast2HeaderAdded)
                        {
                            Breakfast2Panel.Children.Add(CreateHeaderRow());
                            breakfast2HeaderAdded = true;
                        }
                        Breakfast2Panel.Children.Add(rowGrid);
                        break;

                    case "обед":
                        if (!lunchHeaderAdded)
                        {
                            LunchPanel.Children.Add(CreateHeaderRow());
                            lunchHeaderAdded = true;
                        }
                        LunchPanel.Children.Add(rowGrid);
                        break;
                }

            }

            SetSectionsVisibility(breakfastHeaderAdded, breakfast2HeaderAdded, lunchHeaderAdded);
        }

        #endregion


        private Grid CreateHeaderRow()
        {
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                Height = 40,
                Margin = new Thickness(0, 10, 0, 5)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            AddHeaderCell(grid, "Раздел", 0);
            AddHeaderCell(grid, "№рец.", 1);
            AddHeaderCell(grid, "Блюдо", 2);
            AddHeaderCell(grid, "Выход,г", 3);
            AddHeaderCell(grid, "Цена", 4);
            AddHeaderCell(grid, "Ккал", 5);
            AddHeaderCell(grid, "Белки", 6);
            AddHeaderCell(grid, "Жиры", 7);
            AddHeaderCell(grid, "Углеводы", 8);

            return grid;
        }

        private void AddHeaderCell(Grid grid, string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 5, 0),
                FontSize = 14
            };

            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }



        private Grid CreateMenuRow(string section, string recipe, string dish,
                           string output, string price,
                           string calories, string proteins,
                           string fats, string carbs)
        {
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                Margin = new Thickness(0, 2, 0, 2),
                Height = 40
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            AddCell(grid, section, 0);
            AddCell(grid, recipe, 1);
            AddCell(grid, dish, 2);
            AddCell(grid, output, 3);
            AddCell(grid, price, 4);
            AddCell(grid, calories, 5);
            AddCell(grid, proteins, 6);
            AddCell(grid, fats, 7);
            AddCell(grid, carbs, 8);

            return grid;
        }

        private void AddCell(Grid grid, string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 5, 0),
                FontSize = 14
            };

            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }


    }
}
