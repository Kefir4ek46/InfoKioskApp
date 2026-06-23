using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace InfoKioskApp.Views
{
    /// <summary>
    /// Отдельное WPF-окно с WebView2 для админки.
    ///
    /// Особенности:
    ///   - WindowStyle="None" + кастомный title bar с кнопками свернуть/развернуть/закрыть.
    ///   - Drag title bar — перемещение окна (TitleBar_MouseLeftButtonDown).
    ///   - Свой data-фолдер (WebView2Admin) — не конфликтует с киоском.
    ///   - Токен передаётся через URL (?token=XXX), admin.js подхватывает.
    /// </summary>
    public partial class AdminWindow : Window
    {
        private static readonly string WebViewDataFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InfoKioskApp",
                "WebView2Admin"
            );

        private readonly int _port;
        private readonly string _token;
        private bool _isClosing;

        public AdminWindow(int port, string token)
        {
            InitializeComponent();
            _port = port;
            _token = token ?? "";
            Loaded += async (s, e) => await InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataFolder);
                await AdminWebView.EnsureCoreWebView2Async(env);

                var core = AdminWebView.CoreWebView2;

                // DevTools можно оставить включёнными для отладки админки.
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsZoomControlEnabled = false;

                // Блокируем навигацию на внешние ресурсы — только localhost.
                core.NavigationStarting += (s, args) =>
                {
                    try
                    {
                        string uriStr = args.Uri;
                        if (string.IsNullOrEmpty(uriStr)) return;
                        if (uriStr.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                        if (uriStr.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

                        if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri))
                        {
                            if (!uri.IsLoopback)
                            {
                                args.Cancel = true;
                                Console.WriteLine($"[AdminWindow] Blocked navigation to: {uriStr}");
                            }
                        }
                    }
                    catch
                    {
                        args.Cancel = true;
                    }
                };

                // Новые окна открываем в том же WebView (например ссылки в footer).
                core.NewWindowRequested += (s, args) =>
                {
                    args.Handled = true;
                    // Если ссылка на наш localhost — откроем в текущем WebView.
                    try
                    {
                        if (!string.IsNullOrEmpty(args.Uri) &&
                            Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) &&
                            uri.IsLoopback)
                        {
                            core.Navigate(args.Uri);
                        }
                    }
                    catch { }
                };

                // Строим URL с токеном.
                string url = $"http://localhost:{_port}/admin";
                if (!string.IsNullOrEmpty(_token))
                    url += "?token=" + Uri.EscapeDataString(_token);

                Console.WriteLine($"[AdminWindow] Navigate to: {url}");
                core.Navigate(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminWindow] Init failed: {ex.Message}");
                MessageBox.Show(
                    $"Не удалось открыть админку.\n\n{ex.Message}",
                    "Админка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ===== Title bar handlers =====

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Позволяет перетаскивать окно за title bar.
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                try { DragMove(); } catch { }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            try { WindowState = WindowState.Minimized; } catch { }
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }

        private void ToggleMaximize()
        {
            try
            {
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
                else
                    WindowState = WindowState.Maximized;
            }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing) return;
            _isClosing = true;

            try
            {
                AdminWebView?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminWindow] WebView2.Dispose error: {ex.Message}");
            }
        }
    }
}
