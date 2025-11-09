using Microsoft.Web.WebView2.Core;
using System;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views
{
    public partial class SchoolWebsiteView : UserControl
    {
        private readonly string _allowedHost = "obo-afan.gosuslugi.ru"; // ✅ домен твоей школы

        public SchoolWebsiteView()
        {
            InitializeComponent();
            InitWebView();
        }

        private async void InitWebView()
        {
            try
            {
                await WebView.EnsureCoreWebView2Async(null);
                WebView.Source = new Uri("https://obo-afan.gosuslugi.ru");

                // Запрет переходов на другие сайты
                WebView.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    try
                    {
                        var uri = new Uri(e.Uri);
                        if (!uri.Host.Equals(_allowedHost, StringComparison.OrdinalIgnoreCase))
                        {
                            e.Cancel = true;
                            MessageBox.Show("Переход на сторонние сайты запрещён.",
                                            "Безопасность", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    catch
                    {
                        e.Cancel = true;
                    }
                };

                // Запрещаем открытие внешних окон
                WebView.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                };

                WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации WebView2: {ex.Message}");
            }
        }
    }
}

