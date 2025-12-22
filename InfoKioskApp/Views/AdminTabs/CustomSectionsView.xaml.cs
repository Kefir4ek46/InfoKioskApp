using InfoKioskApp.Models;
using InfoKioskApp.Services;
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace InfoKioskApp.Views.AdminTabs
{
    public partial class CustomSectionsView : System.Windows.Controls.UserControl
    {
        private readonly AppConfig _config;

        public CustomSectionsView()
        {
            InitializeComponent();
            _config = ConfigService.LoadConfig();
            LoadSections();
        }

        private void LoadSections()
        {
            SectionsList.ItemsSource = new List<CustomSection>(_config.CustomSections);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CommonOpenFileDialog
            {
                IsFolderPicker = true
            };

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                _ = dlg.FileName;
            }
        }

        private static string DetectType(string folder)
        {
            var files = Directory.GetFiles(folder);
            if (files.Any(f => f.EndsWith(".pdf"))) return "pdf";
            if (files.Any(f => f.EndsWith(".jpg") || f.EndsWith(".png"))) return "image";
            return "mixed";
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (SectionsList.SelectedItem is CustomSection section)
            {
                _config.CustomSections.Remove(section);
                LoadSections();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ConfigService.SaveConfig(_config);
            System.Windows.MessageBox.Show("Пользовательские разделы сохранены!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}