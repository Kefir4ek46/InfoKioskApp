using InfoKioskApp.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace InfoKioskApp
{
    /// <summary>
    /// Главное окно киоска. Содержит единственный WebView2, который грузит
    /// локальный HTML-интерфейс из папки /web/. Вся бизнес-логика осталась
    /// в Services/* — её вызывает JS через window.chrome.webview.postMessage,
    /// а диспетчером сообщений служит WebMessageBridge.
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly string WebViewDataFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InfoKioskApp",
                "WebView2Kiosk"
            );

        private readonly WebMessageBridge _bridge = new();
        private bool _isClosing = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitWebViewAsync();

            // Подключаем обработчик «Назад в киоск» для оверлея сайта школы.
            // Когда пользователь жмёт кнопку или ESC, скрываем оверлей.
            SchoolSiteView.BackRequested += (s, args) =>
            {
                SchoolSiteView.Visibility = Visibility.Collapsed;
            };

            // Подключаем push-канал от RemoteServerService к WebMessageBridge.
            // Когда админка (через HTTP) просит киоск что-то обновить
            // (например, список расширений после установки) — проксируем
            // сообщение в WebView2 через PostWebMessageAsJson.
            RemoteServerService.KioskPushRequested += (payload) =>
            {
                try { _bridge?.PushToJs(payload); } catch (Exception ex) { Console.WriteLine($"[MainWindow] PushToJs failed: {ex.Message}"); }
            };

            // Стартуем встроенный HTTP-сервер ВСЕГДА — даже если в конфиге RemoteAutoStart=false,
            // потому что WebView2 должен грузить kiosk UI с http://localhost:{port}/
            int port = 8080;
            try
            {
                var cfg = ConfigService.LoadConfig();
                port = cfg.RemotePort > 0 ? cfg.RemotePort : 8080;

                if (!RemoteServerService.IsRunning)
                    RemoteServerService.Start(port);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] RemoteServer start failed: {ex.Message}");
                // Если сервер не стартовал — киоск не сможет грузить HTML, выходим
                MessageBox.Show($"Не удалось запустить локальный HTTP-сервер на порту {port}.\n\n{ex.Message}",
                    "InfoKiosk", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            port = RemoteServerService.IsRunning ? RemoteServerService.Port : port;

            // Загружаем плагины (C# и JS) — асинхронно, но не блокируем запуск киоска.
            // PluginManager зарегистрирует обработчики plugin.<id>.* в WebMessageBridge,
            // и к моменту, когда JS вызовет kiosk.call('plugins.list'), всё будет готово.
            try
            {
                string pluginsRoot = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "plugins");
                _ = InfoKioskApp.Plugins.PluginManager.LoadAllAsync(pluginsRoot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] PluginManager load failed: {ex.Message}");
            }

            // Грузим kiosk-страницу с того же порта, что и админка —
            // один процесс обслуживает и киоск, и удалённых админов.
            try
            {
                KioskWebView.CoreWebView2.Navigate($"http://localhost:{port}/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] Navigate failed: {ex.Message}");
            }
        }

        private async Task InitWebViewAsync()
        {
            int attempt = 0;
            while (attempt < 3)
            {
                attempt++;
                try
                {
                    var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataFolder);
                    await KioskWebView.EnsureCoreWebView2Async(env);

                    var core = KioskWebView.CoreWebView2;

                    // Запрещаем лишнее — это киоск, не браузер
                    core.Settings.AreDevToolsEnabled = false;
                    core.Settings.AreDefaultContextMenusEnabled = false;
                    core.Settings.IsZoomControlEnabled = false;
                    core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                    core.Settings.IsPasswordAutosaveEnabled = false;
                    core.Settings.IsGeneralAutofillEnabled = false;

                    // Канал JS ⇄ C#
                    core.WebMessageReceived += OnWebMessageReceived;

                    // Киоск нельзя покинуть: блокируем ВСЕ попытки открыть новое окно
                    // (window.open, target=_blank, middle-click и т.п.). Любая попытка
                    // либо игнорируется, либо открывает URL в ТОМ ЖЕ WebView (чтобы
                    // пользователь остался в приложении).
                    //
                    // ВАЖНО: в WebView2 SDK 1.0.3650.58 свойства NewWindowRequestedEventArgs.Uri
                    // и NavigationStartingEventArgs.Uri имеют тип string, а не System.Uri.
                    // Поэтому парсим строку в Uri через Uri.TryCreate перед доступом к
                    // IsLoopback / Scheme / Port / OriginalString.
                    core.NewWindowRequested += (s, args) =>
                    {
                        try
                        {
                            // Не открываем новое окно в любом случае
                            args.Handled = true;
                            // Если это ссылка на наш же сайт (localhost:порт) — открываем в текущем WebView
                            string uriStr = args.Uri;
                            if (!string.IsNullOrEmpty(uriStr) &&
                                Uri.TryCreate(uriStr, UriKind.Absolute, out var uri) &&
                                uri.IsLoopback &&
                                uri.Port == RemoteServerService.Port)
                            {
                                core.Navigate(uri.OriginalString);
                            }
                            // Внешние URL — просто игнорируем. Киоск не должен уходить наружу.
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MainWindow] NewWindowRequested error: {ex.Message}");
                        }
                    };

                    // Блокируем ВСЕ загрузки файлов — киоск не должен скачивать
                    // файлы (через Downloads UI можно "сбежать" из приложения).
                    // Это касается и сайта школы (там могут быть ссылки на PDF и т.п.).
                    core.DownloadStarting += (s, args) =>
                    {
                        try
                        {
                            Console.WriteLine($"[MainWindow] Blocked download: {args.ResultFilePath}");
                            args.Cancel = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MainWindow] DownloadStarting error: {ex.Message}");
                        }
                    };

                    // Также блокируем навигацию на внешние ресурсы — это киоск.
                    // Разрешаем только localhost (где живёт наш HttpListener)
                    // и сайт школы (obo-afan.gosuslugi.ru) — он открывается
                    // прямо в киоске, без отдельного WebView2.
                    //
                    // ВАЖНО: свойство IsMainFrame отсутствует на CoreWebView2NavigationStartingEventArgs
                    // в некоторых версиях SDK WebView2 (например, 1.0.3650.58). Используем reflection,
                    // чтобы безопасно получить его — если свойства нет, считаем навигацию
                    // главной (блокируем внешнюю). Reflection выбран вместо dynamic, т.к.
                    // dynamic требует Microsoft.CSharp runtime binder, который не подключён в csproj.
                    core.NavigationStarting += (s, args) =>
                    {
                        try
                        {
                            string uriStr = args.Uri;
                            if (string.IsNullOrEmpty(uriStr)) return;
                            // Разрешаем только наш локальный сервер (любой порт localhost)
                            // и about:blank / data: (используется WebView2 внутри).
                            if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri))
                            {
                                if (uri.IsLoopback) return;
                                if (uri.Scheme == "about" || uri.Scheme == "data") return;
                                // Разрешаем сайт школы — он открывается прямо в киоске.
                                if (uri.Host.Equals("obo-afan.gosuslugi.ru", StringComparison.OrdinalIgnoreCase)
                                    || uri.Host.EndsWith(".obo-afan.gosuslugi.ru", StringComparison.OrdinalIgnoreCase))
                                    return;
                            }
                            else
                            {
                                // Не абсолютный URL (например, относительный) — пропускаем,
                                // WebView2 сам его резолвнет.
                                return;
                            }

                            // Пытаемся прочитать IsMainFrame через reflection. Свойство
                            // существует в новых SDK, но отсутствует в 1.0.3650.58.
                            bool isMainFrame = true; // по умолчанию — считаем главной
                            try
                            {
                                var prop = args.GetType().GetProperty("IsMainFrame");
                                if (prop != null)
                                {
                                    var val = prop.GetValue(args);
                                    if (val is bool b) isMainFrame = b;
                                }
                            }
                            catch
                            {
                                // Свойство недоступно — считаем навигацию главной (безопасно).
                                isMainFrame = true;
                            }

                            // Если iframe внутри киоска пытается загрузить внешний ресурс —
                            // это нормально для schoolsite-прокси и iframe-просмотрщика.
                            // Навигацию самого Top-level WebView на внешние ресурсы — блокируем.
                            if (isMainFrame)
                            {
                                args.Cancel = true;
                                Console.WriteLine($"[MainWindow] Blocked top-level navigation to: {uriStr}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MainWindow] NavigationStarting error: {ex.Message}");
                        }
                    };

                    _bridge.AttachWebView(core);

                    // ОЧЕНЬ ВАЖНО: дожидаемся инжекта bridge-скрипта ДО навигации,
                    // иначе window.kiosk не существует на момент запуска app.js,
                    // и киоск зависает на "Загрузка…"
                    await _bridge.EnsureBridgeInjectedAsync();

                    // Пушим в JS данные при изменении конфига на диске
                    _bridge.StartConfigWatcher();
                    return;
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x800700AA)
                {
                    // Профиль уже занят — пересоздаём папку данных и пробуем снова
                    if (Directory.Exists(WebViewDataFolder))
                    {
                        try { Directory.Delete(WebViewDataFolder, true); } catch { }
                    }
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось инициализировать WebView2.\n\n{ex.Message}\n\nУбедитесь, что установлен Microsoft Edge WebView2 Runtime (Evergreen).",
                        "InfoKiosk", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return;
                }
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.TryGetWebMessageAsString();
                _bridge.HandleMessage(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] WebMessageReceived error: {ex.Message}");
            }
        }

        /// <summary>
        /// Корректное закрытие: сначала освобождаем bridge, потом диспозим сам
        /// WPF-контрол WebView2 (CoreWebView2.Close() НЕ существует), и только
        /// потом окно реально закрывается. Это убирает ошибку HRESULT=0x80131c08
        /// при выходе из debug-режима.
        /// </summary>
        // ===== Методы для сайта школы (оверлей поверх киоска) =====

        /// <summary>
        /// Показать сайт школы в отдельном WebView2-оверлее.
        /// Вызывается из WebMessageBridge по сообщению 'schoolsite.show'.
        /// </summary>
        public void ShowSchoolSite(string url = null)
        {
            try
            {
                SchoolSiteView.Visibility = Visibility.Visible;
                SchoolSiteView.OpenSite(url);
                // Устанавливаем фокус — чтобы ESC и клики работали.
                SchoolSiteView.Focusable = true;
                Keyboard.Focus(SchoolSiteView);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] ShowSchoolSite failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Скрыть оверлей сайта школы и вернуться в киоск.
        /// </summary>
        public void HideSchoolSite()
        {
            try
            {
                SchoolSiteView.Visibility = Visibility.Collapsed;
                KioskWebView.Focusable = true;
                Keyboard.Focus(KioskWebView);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] HideSchoolSite failed: {ex.Message}");
            }
        }

        // ===== Метод для админки (отдельное окно) =====

        /// <summary>
        /// Открыть админку в отдельном WPF-окне.
        /// Вызывается из WebMessageBridge по сообщению 'admin.open'.
        /// Киоск остаётся на экране, админка — в отдельном окне поверх.
        /// </summary>
        public void OpenAdminWindow(string token)
        {
            try
            {
                var win = new Views.AdminWindow(RemoteServerService.Port, token);
                win.Show();
                // Активируем окно — чтобы оно появилось поверх киоска.
                win.Activate();
                win.Focus();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] OpenAdminWindow failed: {ex.Message}");
            }
        }

        private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Повторный вызов (после cleanup) — пропускаем, e.Cancel=false по умолчанию → окно закроется
            if (_isClosing) return;
            _isClosing = true;

            // Отменяем немедленное закрытие — мы сделаем его сами после очистки
            e.Cancel = true;

            try { _bridge?.Dispose(); } catch { }

            // Останавливаем HTTP-сервер
            try { RemoteServerService.Stop(); } catch { }

            // Корректно завершаем все плагины (C# — dispose, JS-сторона сама подчистится).
            try { _ = InfoKioskApp.Plugins.PluginManager.ShutdownAsync(); } catch { }

            // Закрываем WebView2.
            // CoreWebView2.Close() НЕ существует. Нужно диспозить сам контрол WPF.
            try
            {
                if (KioskWebView != null)
                {
                    KioskWebView.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] WebView2.Dispose error: {ex.Message}");
            }

            // Небольшая пауза, чтобы фоновые задачи Bridge завершились
            await Task.Delay(150);

            // Теперь реально закрываем окно
            try
            {
                Close();
            }
            catch { }
        }
    }
}
