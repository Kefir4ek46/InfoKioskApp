using System.Windows;
using System.Windows.Controls;
using InfoKioskApp.Services;
using System.Windows.Forms;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class FilesSettingsView : System.Windows.Controls.UserControl
    {
        public FilesSettingsView()
        {
            InitializeComponent();
            LoadConfig();
        }

        private void LoadConfig()
        {
            var config = ConfigService.LoadConfig();
            MediaPathBox.Text = config.MediaPath ?? "";
            DocumentsPathBox.Text = config.DocumentsPath ?? "";
        }

        private void SelectMediaFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    MediaPathBox.Text = dlg.SelectedPath;
                }
            }
        }

        private void SelectDocumentsFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    DocumentsPathBox.Text = dlg.SelectedPath;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigService.LoadConfig();
            config.MediaPath = MediaPathBox.Text.Trim();
            config.DocumentsPath = DocumentsPathBox.Text.Trim();
            ConfigService.SaveConfig(config);

            System.Windows.MessageBox.Show("Пути сохранены ✅", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
