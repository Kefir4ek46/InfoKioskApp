using System;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using System.Windows.Media.Imaging;
using InfoKioskApp.Services;
using InfoKioskApp.Models;

namespace InfoKioskApp.Views
{
    public partial class ChangesView : UserControl
    {
        private readonly AppConfig _config;

        public ChangesView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
            LoadContent();
        }

        private void LoadContent()
        {
            if (!_config.ShowChanges)
            {
                Content = new TextBlock
                {
                    Text = "Модуль 'Изменения' отключён в настройках.",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 18,
                    Margin = new Thickness(20)
                };
                return;
            }

            if (!File.Exists(_config.ChangesPath))
            {
                Content = new TextBlock
                {
                    Text = $"Файл не найден:\n{_config.ChangesPath}",
                    Foreground = System.Windows.Media.Brushes.Orange,
                    FontSize = 18,
                    Margin = new Thickness(20)
                };
                return;
            }

            switch (_config.ChangesType.ToLower())
            {
                case "excel":
                    LoadExcel(_config.ChangesPath);
                    break;

                case "image":
                    LoadImage(_config.ChangesPath);
                    break;

                default:
                    Content = new TextBlock
                    {
                        Text = "Неверный тип ChangesType в настройках (должен быть 'excel' или 'image')",
                        Foreground = System.Windows.Media.Brushes.Red,
                        FontSize = 18,
                        Margin = new Thickness(20)
                    };
                    break;
            }
        }

        private void LoadExcel(string path)
        {
            ExcelContainer.Visibility = Visibility.Visible;

            var dt = new DataTable();

            using (var workbook = new XLWorkbook(path))
            {
                var ws = workbook.Worksheet(1);
                bool headerAdded = false;

                foreach (var row in ws.RowsUsed())
                {
                    if (!headerAdded)
                    {
                        foreach (var cell in row.Cells())
                            dt.Columns.Add(cell.Value.ToString());
                        headerAdded = true;
                    }
                    else
                    {
                        var newRow = dt.NewRow();
                        int i = 0;
                        foreach (var cell in row.Cells())
                        {
                            newRow[i++] = cell.Value.ToString();
                        }
                        dt.Rows.Add(newRow);
                    }
                }
            }

            ChangesTable.ItemsSource = dt.DefaultView;
        }

        private void LoadImage(string path)
        {
            ImageContainer.Visibility = Visibility.Visible;
            BitmapImage img = new();
            img.BeginInit();
            img.UriSource = new Uri(Path.GetFullPath(path));
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            ChangesImage.Source = img;
        }
    }
}
