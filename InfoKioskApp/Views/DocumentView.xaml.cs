using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;
using InfoKioskApp.Services;

namespace InfoKioskApp.Views
{
    public partial class DocumentsView : UserControl
    {
        private readonly string _docsFolder;

        public DocumentsView()
        {
            InitializeComponent();
            var config = ConfigService.LoadConfig();
            _docsFolder = config.DocumentsPath ?? "data/documents";

            if (!Directory.Exists(_docsFolder))
                Directory.CreateDirectory(_docsFolder);

            LoadDocuments();
        }

        private void LoadDocuments()
        {
            DocsButtonsPanel.Children.Clear();

            var files = Directory.GetFiles(_docsFolder);
            if (files.Length == 0)
            {
                ContentArea.Children.Clear();
                ContentArea.Children.Add(new TextBlock
                {
                    Text = "Нет доступных документов",
                    Foreground = Brushes.Gray,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                return;
            }

            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileName(file);
                var btn = CreateDocButton(name, (s, e) => OpenDocument(file));
                DocsButtonsPanel.Children.Add(btn);
            }
        }

        private static Button CreateDocButton(string title, RoutedEventHandler onClick)
        {
            return new Button
            {
                Content = title,
                Margin = new Thickness(5),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Center
            }.Also(b => b.Click += onClick);
        }

        private void OpenDocument(string path)
        {
            string ext = Path.GetExtension(path).ToLower();

            if (ext == ".pdf")
                LoadPdf(path);
            else if (ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".bmp")
                LoadImage(path);
            else if (ext == ".xlsx" || ext == ".xls")
                LoadExcelPreview(path);
            else
                ShowTextFallback(path);
        }

        #region === PDF ===
        private void LoadPdf(string path)
        {
            try
            {
                var viewer = new WebView2
                {
                    Source = new Uri(Path.GetFullPath(path)),
                    Margin = new Thickness(5),
                    
                };

                ContentArea.Children.Clear();
                ContentArea.Children.Add(viewer);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии PDF: {ex.Message}");
            }
        }
        #endregion

        #region === Изображения ===
        private void LoadImage(string path)
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

                var scroll = new ScrollViewer
                {
                    Content = img,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                ContentArea.Children.Clear();
                ContentArea.Children.Add(scroll);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки изображения: {ex.Message}");
            }
        }
        #endregion

        #region === Excel (превью, без редактирования) ===
        private void LoadExcelPreview(string path)
        {
            try
            {
                ContentArea.Children.Clear();
                ContentArea.Children.Add(new TextBlock
                {
                    Text = "Предпросмотр Excel пока не реализован.",
                    Foreground = Brushes.Gray,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения Excel: {ex.Message}");
            }
        }
        #endregion

        #region === Текстовые документы ===
        private void ShowTextFallback(string path)
        {
            try
            {
                string content = File.ReadAllText(path);

                var scroll = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = content,
                        Foreground = Brushes.White,
                        FontSize = 15,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(10)
                    },
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                ContentArea.Children.Clear();
                ContentArea.Children.Add(scroll);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения файла: {ex.Message}");
            }
        }
        #endregion
    }

    // Утилита для лаконичного добавления обработчика
    public static class ButtonExtensions
    {
        public static Button Also(this Button button, Action<Button> setup)
        {
            setup(button);
            return button;
        }
    }
}
