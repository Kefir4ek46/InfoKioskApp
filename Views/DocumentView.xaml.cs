using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views
{
    public partial class DocumentsView : UserControl
    {
        private string _documentsPath = "data/documents";

        public DocumentsView()
        {
            InitializeComponent();
            LoadDocuments();
        }

        private void LoadDocuments()
        {
            if (!Directory.Exists(_documentsPath))
                Directory.CreateDirectory(_documentsPath);

            var files = Directory.GetFiles(_documentsPath);
            DocumentsList.ItemsSource = files;
        }

        private void DocumentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // (по желанию можно добавить предпросмотр)
        }

        private void OpenDocument_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentsList.SelectedItem is string filePath && File.Exists(filePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка открытия файла: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("Выберите документ из списка.");
            }
        }
    }
}

