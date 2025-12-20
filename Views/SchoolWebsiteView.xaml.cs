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

        private bool _initialized;

        public SchoolWebsiteView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            // ❌ Dispose НЕ делаем — WebView остаётся прогретым
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialized)
                return;

            _initialized = true;
            await InitWebViewAsync();
        }

        private async Task InitWebViewAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataFolder);
                await WebView.EnsureCoreWebView2Async(env);

                ConfigureSecurity();

                // 🔥 Прогрев WebView2
                WebView.Source = new Uri("about:blank");
                WebView.Visibility = Visibility.Collapsed;
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x800700AA)
            {
                // 💥 Профиль заблокирован → восстанавливаем
                RecoverProfile();
                _initialized = false;
                await InitWebViewAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации WebView2: {ex.Message}");
            }
        }

        /// <summary>
        /// Вызывать при нажатии кнопки "Сайт"
        /// </summary>
        public void OpenSite()
        {
            WebView.Visibility = Visibility.Visible;
            WebView.Source = new Uri("https://obo-afan.gosuslugi.ru");
        }

        /// <summary>
        /// Закрыть сайт (НЕ уничтожая WebView2)
        /// </summary>
        public void CloseSite()
        {
            WebView.Visibility = Visibility.Collapsed;
        }

        private void ConfigureSecurity()
        {
            var core = WebView.CoreWebView2;

            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;

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

            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
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
                // лог при желании
            }
        }
    }
}
