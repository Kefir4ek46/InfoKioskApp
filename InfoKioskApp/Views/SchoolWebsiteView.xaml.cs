using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace InfoKioskApp.Views
{
    public partial class SchoolWebsiteView : UserControl
    {
        private readonly string _allowedHost = "obo-afan.gosuslugi.ru";

        private static readonly string WebViewDataFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InfoKioskApp",
                "WebView2SchoolSite"
            );

        private bool _isInitialized;

        public SchoolWebsiteView()
        {
            InitializeComponent();
            InitWebViewOnce();
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

                // 🔥 прогрев
                WebView.Source = new Uri("about:blank");
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

        public void OpenSite()
        {
            if (WebView.CoreWebView2 != null)
                WebView.Source = new Uri("https://obo-afan.gosuslugi.ru");
        }

        private void ConfigureSecurity()
        {
            var core = WebView.CoreWebView2;

            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;

            core.NavigationStarting += (s, e) =>
            {
                try
                {
                    var uri = new Uri(e.Uri);
                    if (!uri.Host.Equals(_allowedHost, StringComparison.OrdinalIgnoreCase))
                        e.Cancel = true;
                }
                catch
                {
                    e.Cancel = true;
                }
            };

            core.NewWindowRequested += (s, e) => e.Handled = true;
        }

        private static void RecoverProfile()
        {
            try
            {
                if (Directory.Exists(WebViewDataFolder))
                    Directory.Delete(WebViewDataFolder, true);
            }
            catch { }
        }
    }
}
