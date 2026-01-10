using InfoKioskApp.Models;
using InfoKioskApp.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views
{
    public partial class SchoolWebsiteView : UserControl
    {
        private string? _homeUrl;        // главная страница
        private string? _allowedHost;    // разрешённый хост
        private bool _isInitialized;

        private static readonly string WebViewDataFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InfoKioskApp",
                "WebView2SchoolSite"
            );

        public SchoolWebsiteView()
        {
            InitializeComponent();

            ConfigService.ConfigChanged += ApplyConfig;
            ApplyConfig(ConfigService.Current);

            InitWebViewOnce();
        }

        private void ApplyConfig(AppConfig cfg)
        {
            _homeUrl = cfg.WebsiteUrl;

            if (Uri.TryCreate(_homeUrl, UriKind.Absolute, out var uri))
                _allowedHost = uri.Host;
            else
                _allowedHost = null;

            OpenHome();
        }

        private async void InitWebViewOnce()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;

            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataFolder);
                await WebView.EnsureCoreWebView2Async(env);

                ConfigureSecurity();

                // прогрев
                WebView.Source = new Uri("about:blank");

                OpenHome();
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x800700AA)
            {
                RecoverProfile();
                _isInitialized = false;
                InitWebViewOnce();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка WebView2: {ex.Message}");
            }
        }

        /// <summary>
        /// Всегда открывает главную страницу (сброс навигации)
        /// </summary>
        public void OpenHome()
        {
            if (WebView.CoreWebView2 == null)
                return;

            if (!Uri.TryCreate(_homeUrl, UriKind.Absolute, out var uri))
                return;

            WebView.CoreWebView2.Navigate(uri.ToString());
        }

        private void ConfigureSecurity()
        {
            var core = WebView.CoreWebView2;

            // 🔒 киоск-настройки
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // ❌ запрет новых окон
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
            };

            // ❌ запрет перехода на другие сайты
            core.NavigationStarting += (s, e) =>
            {
                if (string.IsNullOrEmpty(_allowedHost))
                    return;

                try
                {
                    var uri = new Uri(e.Uri);

                    // разрешаем только текущий сайт
                    if (!uri.Host.Equals(_allowedHost, StringComparison.OrdinalIgnoreCase))
                        e.Cancel = true;
                }
                catch
                {
                    e.Cancel = true;
                }
            };
        }

        private static void RecoverProfile()
        {
            try
            {
                if (Directory.Exists(WebViewDataFolder))
                    Directory.Delete(WebViewDataFolder, true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
