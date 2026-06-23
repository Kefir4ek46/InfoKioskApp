using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace InfoKioskApp.Views
{
    /// <summary>
    /// Отдельный полноэкранный WebView2 для отображения сайта школы.
    ///
    /// Зачем отдельный WebView2 (а не iframe через прокси):
    ///   - Сайт школы (obo-afan.gosuslugi.ru) — SPA на Госуслугах. Прокси-iframe
    ///     ломает клиентский роутинг, AJAX-запросы и авторизационные куки.
    ///     При клике на любой раздел внутри iframe пользователь видит 404.
    ///   - Отдельный WebView2 загружает сайт напрямую, без X-Frame-Options.
    ///
    /// Безопасность:
    ///   - NavigationStarting блокирует любой URL не на obo-afan.gosuslugi.ru.
    ///   - NewWindowRequested всегда.Handled = true (никаких всплывающих окон).
    ///   - DevTools / контекстное меню / zoom выключены.
    ///
    /// ВАЖНО про SEHException:
    ///   Ранее инициализация WebView2 падала с SEHException в
    ///   TranslateAndDispatchMessage — это происходило из-за того, что
    ///   EnsureCoreWebView2Async вызывался до полной готовности WPF-окна.
    ///   Теперь:
    ///   1) Инициализация отложена до первого OpenSite (ленивая).
    ///   2) Перед EnsureCoreWebView2Async ждём Dispatcher.Yield(Background),
    ///      чтобы все pending WPF-сообщения обработались.
    ///   3) Каждый шаг обёрнут в try-catch с логированием.
    /// </summary>
    public partial class SchoolWebsiteView : UserControl
    {
        private readonly string _allowedHost = "obo-afan.gosuslugi.ru";

        private static readonly string WebViewDataFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InfoKioskApp",
                "WebView2SchoolSite"
            );

        private bool _initStarted;
        private bool _initDone;
        private bool _initFailed;
        private bool _waitingForSite;
        private string _currentSiteUrl;

        public event EventHandler BackRequested;

        public SchoolWebsiteView()
        {
            InitializeComponent();
            // НЕ инициализируем WebView2 в конструкторе! Это делается
            // лениво в OpenSite(), когда пользователь первый раз открывает
            // сайт школы. К этому моменту основной KioskWebView уже
            // инициализирован, и WPF-окно полностью готово.

            // Подписываемся на Loaded — UserControl должен быть полностью
            // встроен в visual tree до инициализации WebView2.
            Loaded += (s, e) => { _isLoaded = true; };
        }

        private bool _isLoaded;

        /// <summary>
        /// Показать сайт школы. Если WebView2 ещё не инициализирован —
        /// инициализируем сейчас и открываем URL.
        /// </summary>
        public void OpenSite(string url = null)
        {
            // Marshal в UI-поток, если мы не в нём.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OpenSite(url)), DispatcherPriority.Normal);
                return;
            }

            url = string.IsNullOrWhiteSpace(url) ? $"https://{_allowedHost}" : url;

            if (!IsUrlAllowed(url))
            {
                Console.WriteLine($"[SchoolSite] URL not allowed, fallback to default: {url}");
                url = $"https://{_allowedHost}";
            }

            ShowLoading("Открываем сайт школы…");

            // Запускаем async-работу в UI-потоке.
            _ = OpenSiteAsync(url);
        }

        private async Task OpenSiteAsync(string url)
        {
            try
            {
                if (!_initDone)
                {
                    await InitCoreOnUiThreadAsync();
                }

                if (_initFailed || WebView.CoreWebView2 == null)
                {
                    ShowError("Не удалось инициализировать WebView2.",
                              "Убедитесь, что установлен Microsoft Edge WebView2 Runtime (Evergreen).");
                    return;
                }

                _waitingForSite = true;
                _currentSiteUrl = url;
                WebView.Source = new Uri(url);
                Console.WriteLine($"[SchoolSite] Navigate to: {url}");

                // Watchdog-таймаут 30 сек.
                var siteUrlForWatchdog = url;
                _ = Task.Delay(30000).ContinueWith(t =>
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_waitingForSite && _currentSiteUrl == siteUrlForWatchdog
                                && LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
                            {
                                Console.WriteLine("[SchoolSite] 30s timeout");
                                ShowError("Сайт школы долго не отвечает.",
                                          "Проверьте интернет-соединение. Нажмите «← Назад в киоск» и попробуйте снова.");
                            }
                        }));
                    }
                    catch { }
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SchoolSite] OpenSiteAsync exception: {ex.Message}");
                ShowError("Ошибка при открытии сайта.", ex.Message);
            }
        }

        /// <summary>
        /// Инициализация WebView2. ВЫЗЫВАТЬ ТОЛЬКО ИЗ UI-ПОТОКА.
        ///
        /// Ключевой момент: перед EnsureCoreWebView2Async делаем
        /// await Dispatcher.Yield(Background) — это позволяет WPF обработать
        /// все pending сообщения в очереди. Без этого WebView2 может
        /// обратиться к COM-компоненту во время TranslateAndDispatchMessage
        /// и бросить SEHException.
        /// </summary>
        private async Task InitCoreOnUiThreadAsync()
        {
            if (_initStarted) return;
            _initStarted = true;

            // Ждём, пока UserControl не будет встроен в visual tree (Loaded).
            // Без этого WebView2 падает с SEHException в TranslateAndDispatchMessage.
            int loadWait = 0;
            while (!_isLoaded && loadWait < 100)
            {
                await Task.Delay(50);
                loadWait++;
            }
            Console.WriteLine($"[SchoolSite] Loaded wait: {loadWait * 50}ms, isLoaded={_isLoaded}");

            // Даём WPF обработать pending сообщения перед инициализацией.
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Yield();

            int retries = 0;
            while (true)
            {
                try
                {
                    Console.WriteLine("[SchoolSite] Creating WebView2 environment...");
                    var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataFolder);

                    // Пауза между CreateAsync и EnsureCoreWebView2Async.
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    Console.WriteLine("[SchoolSite] EnsureCoreWebView2Async...");
                    await WebView.EnsureCoreWebView2Async(env);

                    // Ждём, пока CoreWebView2 реально появится.
                    int waitCount = 0;
                    while (WebView.CoreWebView2 == null && waitCount < 50)
                    {
                        await Task.Delay(100);
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        waitCount++;
                    }

                    if (WebView.CoreWebView2 == null)
                    {
                        throw new InvalidOperationException("CoreWebView2 is null after EnsureCoreWebView2Async");
                    }

                    ConfigureSecurity();

                    await Dispatcher.Yield(DispatcherPriority.Background);
                    WebView.Source = new Uri("about:blank");

                    _initDone = true;
                    Console.WriteLine("[SchoolSite] WebView2 initialized OK");
                    return;
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x800700AA)
                {
                    if (retries < 3)
                    {
                        retries++;
                        Console.WriteLine($"[SchoolSite] profile locked, retry {retries}: {ex.Message}");
                        RecoverProfile();
                        await Task.Delay(500);
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        continue;
                    }
                    _initFailed = true;
                    Console.WriteLine($"[SchoolSite] profile locked after 3 retries: {ex.Message}");
                    return;
                }
                catch (Exception ex)
                {
                    _initFailed = true;
                    Console.WriteLine($"[SchoolSite] init failed: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"[SchoolSite] stack: {ex.StackTrace}");
                    return;
                }
            }
        }

        private bool IsUrlAllowed(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host.Equals(_allowedHost, StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith("." + _allowedHost, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureSecurity()
        {
            var core = WebView.CoreWebView2;
            if (core == null) return;

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
                    string uriStr = e.Uri;
                    if (string.IsNullOrEmpty(uriStr)) return;
                    if (uriStr.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                    if (uriStr.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

                    if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri))
                    {
                        if (!uri.Host.Equals(_allowedHost, StringComparison.OrdinalIgnoreCase)
                            && !uri.Host.EndsWith("." + _allowedHost, StringComparison.OrdinalIgnoreCase))
                        {
                            e.Cancel = true;
                            Console.WriteLine($"[SchoolSite] Blocked navigation to: {uriStr}");
                        }
                        else
                        {
                            _waitingForSite = true;
                            _currentSiteUrl = uriStr;
                        }
                    }
                }
                catch
                {
                    e.Cancel = true;
                }
            };

            core.NewWindowRequested += (s, e) => e.Handled = true;

            core.NavigationCompleted += (s, e) =>
            {
                try
                {
                    string currentSrc = null;
                    try { currentSrc = WebView.Source?.ToString(); } catch {}

                    bool isBlank = string.IsNullOrEmpty(currentSrc)
                                   || currentSrc.StartsWith("about:", StringComparison.OrdinalIgnoreCase);

                    if (isBlank)
                    {
                        Console.WriteLine("[SchoolSite] NavigationCompleted (about:blank warmup)");
                        return;
                    }

                    if (e.IsSuccess)
                    {
                        Console.WriteLine("[SchoolSite] NavigationCompleted SUCCESS");
                        _waitingForSite = false;
                        if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        Console.WriteLine($"[SchoolSite] Navigation failed: {e.WebErrorStatus}");
                        _waitingForSite = false;
                        ShowError("Сайт школы не загрузился.",
                                  $"Ошибка: {e.WebErrorStatus}. Проверьте подключение к интернету.");
                    }
                }
                catch { }
            };
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

        // ============ UI helpers ============

        private void ShowLoading(string msg)
        {
            try
            {
                if (LoadingIcon != null) LoadingIcon.Text = "⏳";
                if (LoadingText != null) LoadingText.Text = msg;
                if (LoadingSubtext != null) LoadingSubtext.Text = "Это может занять несколько секунд";
                if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;
            }
            catch { }
        }

        private void ShowError(string title, string detail)
        {
            try
            {
                if (LoadingIcon != null) LoadingIcon.Text = "🌐";
                if (LoadingText != null)
                {
                    LoadingText.Text = title;
                    LoadingText.Foreground = System.Windows.Media.Brushes.White;
                }
                if (LoadingSubtext != null)
                {
                    LoadingSubtext.Text = detail ?? "";
                    LoadingSubtext.Foreground = System.Windows.Media.Brushes.Gray;
                }
                if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;
            }
            catch { }
        }

        // ============ UI events ============

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                BackRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }
    }
}
