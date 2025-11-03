using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Diagnostics;

namespace InfoKioskApp.Views
{
    public partial class CustomContentView : UserControl
    {
        private readonly string _folderPath;

        public CustomContentView(string folderPath)
        {
            InitializeComponent();
            _folderPath = folderPath;
            LoadFolderContent();
        }

        private void LoadFolderContent()
        {
            if (!Directory.Exists(_folderPath))
            {
                ContentPanel.Children.Add(new TextBlock
                {
                    Text = "Папка не найдена",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 20,
                    Margin = new Thickness(20)
                });
                return;
            }

            string[] files = Directory.GetFiles(_folderPath);

            foreach (var file in files)
            {
                string ext = System.IO.Path.GetExtension(file).ToLower();

                // Для изображений — показываем превью
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp")
                {
                    var img = new Image
                    {
                        Source = new BitmapImage(new System.Uri(file)),
                        Width = 180,
                        Height = 180,
                        Margin = new Thickness(10),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    img.MouseLeftButtonUp += (s, e) => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                    ContentPanel.Children.Add(img);
                }
                // Для PDF или других файлов — иконка и имя
                else
                {
                    var border = new Border
                    {
                        Width = 180,
                        Height = 180,
                        Margin = new Thickness(10),
                        CornerRadius = new CornerRadius(10),
                        Background = System.Windows.Media.Brushes.DimGray,
                        Child = new TextBlock
                        {
                            Text = System.IO.Path.GetFileName(file),
                            Foreground = System.Windows.Media.Brushes.White,
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(10)
                        },
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    border.MouseLeftButtonUp += (s, e) => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                    ContentPanel.Children.Add(border);
                }
            }
        }
    }
}
