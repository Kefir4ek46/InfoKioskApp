using InfoKioskApp.Models;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace InfoKioskApp.Services
{
    /// <summary>
    /// Диспетчер сообщений между JS (в WebView2) и C#-сервисами.
    ///
    /// Протокол (JSON):
    ///   Запрос  JS → C#:   { "id": "msg-1", "type": "config.get", "data": {...} }
    ///   Ответ   C# → JS:   { "id": "msg-1", "ok": true,  "data": {...} }
    ///                       { "id": "msg-1", "ok": false, "error": "..." }
    ///   Пуш     C# → JS:   { "type": "config.changed", "data": {...} }
    ///
    /// Вызов из JS:
    ///   const reply = await window.kiosk.call('config.get', { ... });
    ///   window.kiosk.on('config.changed', (data) => { ... });
    /// </summary>
    public sealed class WebMessageBridge : IDisposable
    {
        private CoreWebView2 _core;
        private Dispatcher _dispatcher;
        private FileSystemWatcher _configWatcher;
        private FileSystemWatcher _dataWatcher;
        private bool _disposed;
        private bool _bridgeInjected;

        // --------------------------- ATTACH / DISPOSE ---------------------------

        public void AttachWebView(CoreWebView2 core)
        {
            _core = core;
            _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        /// <summary>
        /// Инжектит helper-объект window.kiosk ДО загрузки любых наших скриптов.
        /// ВАЖНО: этот метод нужно дождаться (await) перед Navigate().
        /// </summary>
        public async Task EnsureBridgeInjectedAsync()
        {
            if (_core == null) return;
            if (_bridgeInjected) return;
            try
            {
                await _core.AddScriptToExecuteOnDocumentCreatedAsync(KioskBridgeScript);
                _bridgeInjected = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bridge] AddScriptToExecuteOnDocumentCreatedAsync failed: {ex.Message}");
            }
        }

        public void StartConfigWatcher()
        {
            try
            {
                string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                Directory.CreateDirectory(dataDir);

                _configWatcher = new FileSystemWatcher(dataDir, "config.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false
                };
                _configWatcher.Changed += async (s, e) => await OnConfigChangedAsync();
                _configWatcher.Created += async (s, e) => await OnConfigChangedAsync();

                // Широкий ватч на изменения в data/ (медиа, новости, почёт, календарь) —
                // браузер сам решит, перечитывать ли данные.
                _dataWatcher = new FileSystemWatcher(dataDir)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true
                };
                _dataWatcher.Changed += OnDataChanged;
                _dataWatcher.Created += OnDataChanged;
                _dataWatcher.Deleted += OnDataChanged;
                _dataWatcher.Renamed += OnDataChanged;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bridge] ConfigWatcher init failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _configWatcher?.Dispose(); } catch { }
            try { _dataWatcher?.Dispose(); } catch { }
        }

        // --------------------------- MESSAGE LOOP ---------------------------

        public void HandleMessage(string json)
        {
            try
            {
                var msg = JObject.Parse(json);
                string id = (string)msg["id"] ?? "";
                string type = (string)msg["type"] ?? "";
                JToken data = msg["data"] ?? new JObject();

                // Тяжёлые операции — в фоне, чтобы не блокировать UI-поток WebView
                _ = Task.Run(async () =>
                {
                    object reply;
                    try
                    {
                        var result = await DispatchAsync(type, data);
                        reply = new { id, ok = true, data = result };
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Bridge] DispatchAsync error for '{type}': {ex.Message}");
                        reply = new { id, ok = false, error = ex.Message };
                    }
                    SendToJs(reply);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bridge] HandleMessage failed: {ex.Message}");
            }
        }

        private Task<object> DispatchAsync(string type, JToken data)
        {
            try
            {
                switch (type)
                {
                    // === Конфигурация ===
                    case "config.get":   return Task.FromResult<object>(ConfigService.LoadConfig());
                    case "config.save":
                        ConfigService.SaveConfig(data.ToObject<AppConfig>());
                        return Task.FromResult<object>(true);

                    // === Часы / системная информация ===
                    case "system.now":
                        return Task.FromResult<object>(new { iso = DateTime.Now.ToString("o"), tz = TimeZoneInfo.Local.DisplayName });

                    case "system.info":
                        return Task.FromResult<object>(SystemInfoService.GetInfo());

                    // === Расписание звонков ===
                    case "bell.get":
                        return Task.FromResult<object>(LoadBellSchedule());

                    case "bell.save":
                    {
                        var bellData = data as JObject ?? (data != null ? JObject.FromObject(data) : new JObject());
                        return Task.FromResult<object>(SaveBellSchedule(bellData));
                    }

                    // === Расписание уроков ===
                    case "schedule.main":
                        return Task.FromResult<object>(ReadScheduleWithSheets(GetMainSchedulePath(), 0));

                    case "schedule.changes":
                        return Task.FromResult<object>(ReadScheduleWithSheets(GetChangesPath(), 0));

                    // Чтение конкретного листа расписания (main/changes/other).
                    case "schedule.sheet":
                    {
                        string which = (string)data?["which"] ?? "main";
                        int sheetIdx = (int?)data?["sheet"] ?? 0;
                        string path = which == "changes" ? GetChangesPath() : GetMainSchedulePath();
                        return Task.FromResult<object>(ReadScheduleWithSheets(path, sheetIdx));
                    }

                    // Информация о файле изменённого расписания (тип, URL для просмотра).
                    case "schedule.changes.info":
                    {
                        string changesPath = GetChangesPath();
                        bool exists = File.Exists(changesPath);
                        string ext = exists ? Path.GetExtension(changesPath).ToLowerInvariant() : "";
                        string url = exists ? $"/download?target=changes&name={Uri.EscapeDataString(Path.GetFileName(changesPath))}" : "";
                        return Task.FromResult<object>(new { exists, ext, url, fileName = exists ? Path.GetFileName(changesPath) : "", path = changesPath });
                    }

                    // Рендер DOCX файла изменённого расписания в HTML.
                    case "schedule.changes.docx":
                    {
                        string changesPath = GetChangesPath();
                        if (!File.Exists(changesPath))
                            return Task.FromResult<object>(new { html = "", hasTable = false, error = "Файл не найден" });
                        return Task.FromResult<object>(DocxToHtmlConverter.Convert(changesPath));
                    }

                    // Рендер DOCX файла доп. расписания в HTML.
                    case "schedule.other.docx":
                    {
                        string name = (string)data?["name"] ?? "";
                        string folder = OtherSchedulesFolder;
                        string safeName = Path.GetFileName(name);
                        string path = Path.Combine(folder, safeName);
                        if (!File.Exists(path))
                            return Task.FromResult<object>(new { html = "", hasTable = false, error = "Файл не найден" });
                        return Task.FromResult<object>(DocxToHtmlConverter.Convert(path));
                    }

                    // Список дополнительных расписаний
                    case "schedule.other.list":
                        return Task.FromResult<object>(ListOtherSchedules());

                    // Читает конкретное доп. расписание по имени файла.
                    case "schedule.other.get":
                    {
                        string name = (string)data?["name"] ?? "";
                        int otherSheetIdx = (int?)data?["sheet"] ?? 0;
                        return Task.FromResult<object>(ReadOtherScheduleWithSheets(name, otherSheetIdx));
                    }

                    // Информация о доп. расписании (тип файла, URL).
                    case "schedule.other.info":
                    {
                        string name = (string)data?["name"] ?? "";
                        string folder = OtherSchedulesFolder;
                        string safeName = Path.GetFileName(name);
                        string path = Path.Combine(folder, safeName);
                        bool exists = File.Exists(path);
                        string ext = exists ? Path.GetExtension(path).ToLowerInvariant() : "";
                        string url = exists ? $"/download?target=other&name={Uri.EscapeDataString(safeName)}" : "";
                        return Task.FromResult<object>(new { exists, ext, url, fileName = safeName });
                    }

                    // === Столовая ===
                    case "canteen.menu":
                    {
                        string date = (string)data?["date"] ?? DateTime.Today.ToString("yyyy-MM-dd");
                        return Task.FromResult<object>(LoadCanteenMenu(date));
                    }

                    // === Календарь ===
                    case "calendar.list":
                        return Task.FromResult<object>(CalendarService.LoadEvents());

                    // === Новости (только опубликованные — для киоска) ===
                    case "news.list":
                        return Task.FromResult<object>(LoadPublishedNews());

                    // === Почётная доска ===
                    case "honor.list":
                        return Task.FromResult<object>(LoadHonor());

                    // === Медиа-галерея ===
                    case "media.categories":
                        return Task.FromResult<object>(ListMediaCategories());

                    case "media.posts":
                    {
                        string cat = (string)data?["category"] ?? "";
                        int page = (int?)data?["page"] ?? 1;
                        int size = (int?)data?["pageSize"] ?? 12;
                        return Task.FromResult<object>(ListMediaPosts(cat, page, size));
                    }

                    case "media.post":
                    {
                        string pcat = (string)data?["category"] ?? "";
                        string pid = (string)data?["id"] ?? "";
                        return Task.FromResult<object>(GetMediaPost(pcat, pid));
                    }

                    // === Документы ===
                    case "documents.list":
                        return Task.FromResult<object>(ListDocuments());

                    // === Просмотр XLSX-файлов с навигацией по листам ===
                    // data: { name: "file.xlsx", sheet: 0 }
                    // Возвращает { sheets: ["Лист1", "Лист2"], currentSheet: 0,
                    //             columns: [...], rows: [[...], ...] }
                    case "documents.xlsx.view":
                    {
                        string dname = (string)data?["name"] ?? "";
                        int sheetIdx = (int?)data?["sheet"] ?? 0;
                        return Task.FromResult<object>(ReadXlsxWithSheets(dname, sheetIdx));
                    }

                    // === Погода ===
                    case "weather.get":
                    {
                        string city = (string)data?["city"] ?? ConfigService.LoadConfig().City ?? "Warsaw";
                        return GetWeatherAsync(city);
                    }

                    // === Сброс закешированных координат (при смене города в админке) ===
                    case "weather.reset":
                    {
                        try
                        {
                            var cfg = ConfigService.LoadConfig();
                            cfg.CachedLatitude = 0;
                            cfg.CachedLongitude = 0;
                            ConfigService.SaveConfig(cfg);
                            Console.WriteLine("[Bridge] weather coordinates reset");
                            return Task.FromResult<object>(new { ok = true });
                        }
                        catch (Exception ex)
                        {
                            return Task.FromResult<object>(new { ok = false, error = ex.Message });
                        }
                    }

                    // === Файл-операции (для киоска — только чтение) ===
                    case "file.url":
                    {
                        string target = (string)data?["target"] ?? "media";
                        string name = (string)data?["name"] ?? "";
                        string category = (string)data?["category"] ?? "";
                        string post = (string)data?["post"] ?? "";
                        return Task.FromResult<object>(BuildFileUrl(target, name, category, post));
                    }

                    // === Пользовательские разделы ===
                    case "customsections.list":
                        return Task.FromResult<object>(ConfigService.LoadConfig().CustomSections ?? new List<CustomSection>());

                    case "customsection.files":
                    {
                        string folder = (string)data?["folderPath"] ?? "";
                        return Task.FromResult<object>(ListCustomSectionFiles(folder));
                    }

                    // === Idle-экран: список изображений ===
                    case "idle.images":
                        return Task.FromResult<object>(ListIdleImages());

                    // === Запрос на fullscreen (JS не может сам — делает C#) ===
                    case "window.fullscreen":
                        bool enable = data?["enable"]?.ToObject<bool>() ?? true;
                        SetFullscreen(enable);
                        return Task.FromResult<object>(true);

                    // === Перезагрузка страницы киоска (например, после смены темы) ===
                    case "window.reload":
                        _core?.Reload();
                        return Task.FromResult<object>(true);

                    // === Управление окном киоска (свернуть / закрыть) ===
                    case "window.minimize":
                    {
                        var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                        disp?.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var win = System.Windows.Application.Current?.MainWindow;
                                if (win != null) win.WindowState = System.Windows.WindowState.Minimized;
                            }
                            catch (Exception ex) { Console.WriteLine($"[Bridge] minimize failed: {ex.Message}"); }
                        }));
                        return Task.FromResult<object>(new { ok = true });
                    }

                    case "window.close":
                    {
                        var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                        disp?.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var win = System.Windows.Application.Current?.MainWindow;
                                if (win != null) win.Close();
                            }
                            catch (Exception ex) { Console.WriteLine($"[Bridge] close failed: {ex.Message}"); }
                        }));
                        return Task.FromResult<object>(new { ok = true });
                    }

                    // === Программная навигация киоска на новый URL ===
                    // Используется admin-access.js для надёжного перехода на /admin
                    // после ввода PIN-кода (window.location.href в WebView2 иногда
                    // не срабатывает из-за настроек безопасности/навигации).
                    // Разрешаем только loopback URL (http://localhost:порт/...),
                    // чтобы киоск нельзя было «увести» на внешний ресурс.
                    case "window.navigate":
                    {
                        string navUrl = (string)data?["url"] ?? "";
                        if (string.IsNullOrWhiteSpace(navUrl))
                        {
                            Console.WriteLine("[Bridge] window.navigate: empty url");
                            return Task.FromResult<object>(new { ok = false, error = "empty url" });
                        }
                        try
                        {
                            // Разрешаем только относительные URL или loopback
                            Uri uri;
                            bool allowed;
                            if (navUrl.StartsWith("/", StringComparison.Ordinal) || navUrl.StartsWith("./", StringComparison.Ordinal))
                            {
                                // Относительный URL — строим абсолютный от текущего Source.
                                // Если Source вдруг null ( WebView ещё не загрузился ),
                                // используем дефолтный http://localhost:{port}/.
                                string baseUri;
                                try
                                {
                                    baseUri = _core?.Source?.ToString();
                                }
                                catch
                                {
                                    baseUri = null;
                                }
                                if (string.IsNullOrWhiteSpace(baseUri))
                                    baseUri = $"http://localhost:{RemoteServerService.Port}/";

                                uri = new Uri(new Uri(baseUri), navUrl);
                                allowed = uri.IsLoopback;
                            }
                            else if (Uri.TryCreate(navUrl, UriKind.Absolute, out uri))
                            {
                                allowed = uri.IsLoopback;
                            }
                            else
                            {
                                Console.WriteLine($"[Bridge] window.navigate: invalid url '{navUrl}'");
                                return Task.FromResult<object>(new { ok = false, error = "invalid url" });
                            }
                            if (!allowed)
                            {
                                Console.WriteLine($"[Bridge] window.navigate: external url blocked '{navUrl}'");
                                return Task.FromResult<object>(new { ok = false, error = "external url blocked" });
                            }

                            string targetUrl = uri.ToString();
                            Console.WriteLine($"[Bridge] window.navigate → {targetUrl}");

                            var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                            if (disp == null)
                            {
                                try { _core?.Navigate(targetUrl); }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Bridge] Navigate failed (no dispatcher): {ex.Message}");
                                    return Task.FromResult<object>(new { ok = false, error = ex.Message });
                                }
                            }
                            else
                            {
                                disp.BeginInvoke(new Action(() =>
                                {
                                    try { _core?.Navigate(targetUrl); }
                                    catch (Exception ex) { Console.WriteLine($"[Bridge] Navigate failed (dispatched): {ex.Message}"); }
                                }));
                            }
                            return Task.FromResult<object>(new { ok = true, url = targetUrl });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Bridge] window.navigate exception: {ex.Message}");
                            return Task.FromResult<object>(new { ok = false, error = ex.Message });
                        }
                    }

                    // === Пустой ping — чтобы JS мог проверить, что bridge жив ===
                    case "ping":
                        return Task.FromResult<object>(new { pong = true, time = DateTime.Now.ToString("o") });

                    // === Открыть админку в отдельном WPF-окне ===
                    // После ввода PIN на киоске admin-access.js вызывает этот метод,
                    // передавая токен. C# создаёт AdminWindow с WebView2, грузит
                    // /admin?token=XXX. Киоск остаётся на месте.
                    case "admin.open":
                    {
                        string token = (string)data?["token"] ?? "";
                        var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                        if (disp == null)
                            return Task.FromResult<object>(new { ok = false, error = "no dispatcher" });
                        disp.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var win = System.Windows.Application.Current?.MainWindow as MainWindow;
                                win?.OpenAdminWindow(token);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Bridge] admin.open failed: {ex.Message}");
                            }
                        }));
                        return Task.FromResult<object>(new { ok = true });
                    }

                    // === Сайт школы — навигация в основном WebView киоска ===
                    // Вместо отдельного WPF-оверлея (который вызывал SEHException),
                    // навигируем основной KioskWebView на сайт школы.
                    // NavigationStarting в MainWindow разрешает obo-afan.gosuslugi.ru.
                    // После загрузки сайта инжектируем плавающую кнопку "← Назад в киоск".
                    case "schoolsite.navigate":
                    {
                        string siteUrl = (string)data?["url"] ?? "https://obo-afan.gosuslugi.ru";
                        Console.WriteLine($"[Bridge] schoolsite.navigate → {siteUrl}");
                        var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                        if (disp == null)
                            return Task.FromResult<object>(new { ok = false, error = "no dispatcher" });
                        int kioskPort = RemoteServerService.Port;
                        disp.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Навигируем основной WebView киоска на сайт школы.
                                _core?.Navigate(siteUrl);

                                // Инжектируем скрипт через AddScriptToExecuteOnDocumentCreatedAsync —
                                // он выполнится при загрузке каждой страницы. Скрипт сам проверяет,
                                // на каком домене он находится, и показывает кнопку только на сайте школы.
                                string script = $@"
(function() {{
  if (window.__kioskBackBtnInstalled) return;
  window.__kioskBackBtnInstalled = true;
  function tryAddButton() {{
    if (!document.body) {{ setTimeout(tryAddButton, 100); return; }}
    var host = window.location.hostname || '';
    if (host.indexOf('obo-afan.gosuslugi.ru') < 0) return;
    if (document.getElementById('__kiosk_back_btn__')) return;
    var btn = document.createElement('div');
    btn.id = '__kiosk_back_btn__';
    btn.innerHTML = '⬅ Назад в киоск';
    btn.style.cssText = 'position:fixed;top:20px;left:20px;z-index:999999;background:linear-gradient(135deg,#3A9FFF 0%,#2A82DA 100%);color:#fff;padding:18px 32px;border-radius:14px;border:2px solid rgba(255,255,255,0.3);font-family:Roboto,Arial,sans-serif;font-size:22px;font-weight:700;cursor:pointer;box-shadow:0 8px 28px rgba(0,0,0,0.5),0 0 0 1px rgba(255,255,255,0.1);backdrop-filter:blur(10px);user-select:none;line-height:1;letter-spacing:0.5px;min-height:60px;display:flex;align-items:center;gap:8px;';
    btn.onmouseenter = function() {{ btn.style.transform = 'scale(1.05)'; btn.style.boxShadow = '0 12px 36px rgba(58,159,255,0.6)'; }};
    btn.onmouseleave = function() {{ btn.style.transform = 'scale(1)'; btn.style.boxShadow = '0 8px 28px rgba(0,0,0,0.5)'; }};
    btn.onclick = function() {{
      window.location.href = 'http://localhost:{kioskPort}/';
    }};
    document.body.appendChild(btn);
  }}
  tryAddButton();
  setInterval(tryAddButton, 2000);
}})();
";
                                _core?.AddScriptToExecuteOnDocumentCreatedAsync(script);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Bridge] schoolsite.navigate failed: {ex.Message}");
                            }
                        }));
                        return Task.FromResult<object>(new { ok = true });
                    }

                    // === Сайт школы в отдельном WPF-оверлее (не через прокси-iframe) ===
                    // schoolsite.show — показать оверлей и загрузить сайт.
                    //   data: { url?: string }  url по умолчанию = https://obo-afan.gosuslugi.ru
                    // schoolsite.hide — скрыть оверлей, вернуться в киоск.
                    //
                    // ВАЖНО: показ/скрытие WPF-контрола должно делаться в UI-потоке.
                    // Dispatch через Dispatcher.BeginInvoke.
                    case "schoolsite.show":
                    {
                        string siteUrl = (string)data?["url"] ?? "";
                        var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                        if (disp == null)
                            return Task.FromResult<object>(new { ok = false, error = "no dispatcher" });
                        disp.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var win = System.Windows.Application.Current?.MainWindow as MainWindow;
                                win?.ShowSchoolSite(string.IsNullOrWhiteSpace(siteUrl) ? null : siteUrl);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Bridge] schoolsite.show failed: {ex.Message}");
                            }
                        }));
                        return Task.FromResult<object>(new { ok = true });
                    }

                    case "schoolsite.hide":
                    {
                        var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                        if (disp == null)
                            return Task.FromResult<object>(new { ok = false, error = "no dispatcher" });
                        disp.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var win = System.Windows.Application.Current?.MainWindow as MainWindow;
                                win?.HideSchoolSite();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Bridge] schoolsite.hide failed: {ex.Message}");
                            }
                        }));
                        return Task.FromResult<object>(new { ok = true });
                    }

                    // === Плагины ===
                    // "plugins.list" — список JS-плагинов (для динамического сайдбара).
                    // "plugin.<id>.<method>" — вызов метода C#-плагина (если есть).
                    case "plugins.list":
                        return Task.FromResult<object>(InfoKioskApp.Plugins.PluginManager.GetJsPluginDescriptors());

                    default:
                        // Маршрутизация вызовов к C#-плагинам: plugin.<id>.<method>
                        if (type.StartsWith("plugin.", StringComparison.Ordinal))
                        {
                            JObject pluginArgs = data as JObject ?? (data != null ? JObject.FromObject(data) : new JObject());
                            return InfoKioskApp.Plugins.PluginManager.TryHandleCall(type, pluginArgs);
                        }
                        throw new InvalidOperationException($"Unknown message type: {type}");
                }
            }
            catch (Exception ex)
            {
                // Возвращаем ошибку вместо падения. ВАЖНО: поле ok=false
                // обязательно — иначе HandleMessage обернёт это в {ok:true, data:{...}}
                // и JS подумает, что вызов успешен.
                Console.WriteLine($"[Bridge] DispatchAsync outer catch for '{type}': {ex.Message}");
                return Task.FromResult<object>(new { ok = false, error = ex.Message });
            }
        }

        private static async Task<object> GetWeatherAsync(string city)
        {
            try
            {
                var w = await WeatherService.GetWeatherAsync(city);
                return w;
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // --------------------------- PUSH TO JS ---------------------------

        /// <summary>
        /// Отправляет произвольное push-сообщение в WebView2 (киоск).
        /// Используется внешними сервисами (например, RemoteServerService),
        /// чтобы уведомить киоск об изменениях без перезагрузки страницы.
        /// payload должен иметь вид { type: "...", data: {...} }.
        /// </summary>
        public void PushToJs(object payload)
        {
            SendToJs(payload);
        }

        private async void SendToJs(object payload)
        {
            if (_core == null) return;
            try
            {
                string json = JsonConvert.SerializeObject(payload, Formatting.None);
                // Must be on UI thread
                var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                if (disp == null)
                {
                    try { _core.PostWebMessageAsJson(json); } catch { }
                    return;
                }
                await disp.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _core.PostWebMessageAsJson(json);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Bridge] PostWebMessageAsJson failed: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bridge] SendToJs failed: {ex.Message}");
            }
        }

        private async Task OnConfigChangedAsync()
        {
            // Дебаунс: файл мог сохраняться несколько раз подряд
            await Task.Delay(150);
            try
            {
                var cfg = ConfigService.LoadConfig();
                SendToJs(new { type = "config.changed", data = cfg });
            }
            catch { }
        }

        private void OnDataChanged(object sender, FileSystemEventArgs e)
        {
            // Дебаунс через простой таймер — пушим не чаще раза в 500мс
            if ((DateTime.UtcNow - _lastDataPush).TotalMilliseconds < 500) return;
            _lastDataPush = DateTime.UtcNow;

            // Определяем, какой раздел изменился, по пути
            string rel = e.FullPath.Replace(AppDomain.CurrentDomain.BaseDirectory, "").Replace('\\', '/').ToLowerInvariant();
            string section =
                rel.Contains("/news/") ? "news" :
                rel.Contains("/honor/") ? "honor" :
                rel.Contains("/media/") ? "media" :
                rel.Contains("/calendar") ? "calendar" :
                rel.Contains("/canteenmenu/") ? "canteen" :
                rel.Contains("/documents/") ? "documents" :
                rel.Contains("/schedules/") ? "schedule" :
                rel.Contains("bellschedule") ? "bell" :
                "other";

            SendToJs(new { type = "data.changed", data = new { section, path = rel } });
        }
        private DateTime _lastDataPush = DateTime.MinValue;

        // ----------------=========== SERVICE HELPERS ===================-----------

        private static string DataRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        // === Расписание звонков ===
        private static object LoadBellSchedule()
        {
            try
            {
                string path = Path.Combine(DataRoot, "BellSchedule.json");
                if (!File.Exists(path))
                {
                    // Возвращаем пустой активный вариант.
                    return new
                    {
                        active = "default",
                        variants = new Dictionary<string, object>
                        {
                            ["default"] = new { name = "Обычный день", lessons = new object[0] }
                        }
                    };
                }
                var json = File.ReadAllText(path);
                var parsed = JObject.Parse(json);

                // Новый формат: { active, variants: { id: {name, lessons} } }
                if (parsed["variants"] != null)
                {
                    // Возвращаем весь объект — киоск сам выберет активный вариант.
                    return parsed;
                }

                // Старый формат: { lessons: [...] } — конвертируем.
                var lessons = parsed["lessons"] as JArray ?? new JArray();
                return new
                {
                    active = "default",
                    variants = new Dictionary<string, object>
                    {
                        ["default"] = new { name = "Обычный день", lessons }
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bell] LoadBellSchedule error: {ex.Message}");
                return new
                {
                    active = "default",
                    variants = new Dictionary<string, object>
                    {
                        ["default"] = new { name = "Обычный день", lessons = new object[0] }
                    },
                    error = ex.Message
                };
            }
        }

        private static object SaveBellSchedule(JObject data)
        {
            try
            {
                string path = Path.Combine(DataRoot, "BellSchedule.json");
                Directory.CreateDirectory(DataRoot);
                File.WriteAllText(path, data.ToString(Formatting.Indented));
                Console.WriteLine("[Bell] BellSchedule.json saved");
                return new { ok = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bell] SaveBellSchedule error: {ex.Message}");
                return new { ok = false, error = ex.Message };
            }
        }

        // === Расписание уроков ===
        // Ищем файл в data/schedules/main/ — первый .xlsx/.xls файл.
        // Если нет — fallback на config.MainSchedulePath.
        private static string GetMainSchedulePath()
        {
            string schedulesMainDir = Path.Combine(DataRoot, "schedules", "main");
            if (Directory.Exists(schedulesMainDir))
            {
                var file = Directory.GetFiles(schedulesMainDir, "*.xlsx")
                    .Concat(Directory.GetFiles(schedulesMainDir, "*.xls"))
                    .FirstOrDefault();
                if (file != null) return file;
            }
            // Fallback на старый путь из конфига.
            var cfg = ConfigService.LoadConfig();
            string p = string.IsNullOrWhiteSpace(cfg.MainSchedulePath) ? "data/schedules/main/MainSchedule.xlsx" : cfg.MainSchedulePath;
            return Path.IsPathRooted(p) ? p : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, p);
        }

        // Ищем файл в data/schedules/changes/ — любой поддерживаемый формат.
        private static string GetChangesPath()
        {
            string schedulesChangesDir = Path.Combine(DataRoot, "schedules", "changes");
            if (Directory.Exists(schedulesChangesDir))
            {
                var file = Directory.GetFiles(schedulesChangesDir)
                    .Where(f => {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext == ".xlsx" || ext == ".xls" || ext == ".pdf" ||
                               ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                               ext == ".gif" || ext == ".webp" || ext == ".bmp" ||
                               ext == ".docx" || ext == ".doc";
                    })
                    .FirstOrDefault();
                if (file != null) return file;
            }
            // Fallback на старый путь из конфига.
            var cfg = ConfigService.LoadConfig();
            string p = string.IsNullOrWhiteSpace(cfg.ChangesPath) ? "data/schedules/changes/Changes.xlsx" : cfg.ChangesPath;
            return Path.IsPathRooted(p) ? p : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, p);
        }

        // === Расписание с поддержкой нескольких листов ===
        // Возвращает { sheets: ["Лист1",...], currentSheet, columns, rows }
        private static object ReadScheduleWithSheets(string xlsxPath, int sheetIdx)
        {
            try
            {
                if (!File.Exists(xlsxPath))
                    return new { columns = new string[0], rows = new object[0], sheets = new string[0], currentSheet = 0, error = "Файл не найден: " + Path.GetFileName(xlsxPath) };

                using var wb = new ClosedXML.Excel.XLWorkbook(xlsxPath);
                var sheetNames = wb.Worksheets.Select(w => w.Name).ToList();
                if (sheetNames.Count == 0)
                    return new { columns = new string[0], rows = new object[0], sheets = new string[0], currentSheet = 0, error = "Нет листов" };

                if (sheetIdx < 0 || sheetIdx >= sheetNames.Count) sheetIdx = 0;
                var ws = wb.Worksheets.ElementAt(sheetIdx);
                var used = ws.RangeUsed();
                if (used == null)
                    return new { columns = new string[0], rows = new object[0], sheets = sheetNames, currentSheet = sheetIdx };

                int colCount = used.ColumnCount();
                int rowCount = used.RowCount();

                var columns = new List<string>();
                for (int c = 1; c <= colCount; c++)
                    columns.Add(ws.Cell(1, c).GetString());

                var rows = new List<List<string>>();
                for (int r = 2; r <= rowCount; r++)
                {
                    var row = new List<string>();
                    for (int c = 1; c <= colCount; c++)
                        row.Add(ws.Cell(r, c).GetString());
                    rows.Add(row);
                }

                return new { columns, rows, sheets = sheetNames, currentSheet = sheetIdx };
            }
            catch (Exception ex)
            {
                return new { columns = new string[0], rows = new object[0], sheets = new string[0], currentSheet = 0, error = ex.Message };
            }
        }

        private static object ReadScheduleTable(string xlsxPath)
        {
            try
            {
                if (!File.Exists(xlsxPath))
                    return new { columns = new string[0], rows = new object[0], error = "Файл не найден: " + Path.GetFileName(xlsxPath) };

                using var wb = new ClosedXML.Excel.XLWorkbook(xlsxPath);
                var ws = wb.Worksheets.First();
                var used = ws.RangeUsed();
                if (used == null)
                    return new { columns = new string[0], rows = new object[0] };

                int colCount = used.ColumnCount();
                int rowCount = used.RowCount();

                var columns = new List<string>();
                for (int c = 1; c <= colCount; c++)
                    columns.Add(ws.Cell(1, c).GetString());

                var rows = new List<List<string>>();
                for (int r = 2; r <= rowCount; r++)
                {
                    var row = new List<string>();
                    for (int c = 1; c <= colCount; c++)
                        row.Add(ws.Cell(r, c).GetString());
                    rows.Add(row);
                }

                return new { columns, rows };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Дополнительные расписания (data/schedules/other/*.xlsx) ===
        // Возвращает список файлов с именами и датой изменения.
        // Используется в киоске для вкладок "Другое расписание".
        private static string OtherSchedulesFolder =>
            Path.Combine(DataRoot, "schedules", "other");

        private static object ListOtherSchedules()
        {
            try
            {
                string folder = OtherSchedulesFolder;
                if (!Directory.Exists(folder))
                    return new { files = new object[0] };

                var files = Directory.GetFiles(folder, "*.xlsx")
                    .Concat(Directory.GetFiles(folder, "*.xls"))
                    .Select(f => new
                    {
                        name = Path.GetFileName(f),
                        fullName = f,
                        dateModified = File.GetLastWriteTime(f)
                    })
                    .OrderByDescending(f => f.dateModified)
                    .Select(f => new { f.name, f.dateModified })
                    .ToList();

                return new { files };
            }
            catch (Exception ex)
            {
                return new { files = new object[0], error = ex.Message };
            }
        }

        private static object ReadOtherSchedule(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return new { columns = new string[0], rows = new object[0], error = "Имя файла не указано" };

                // Защита от path traversal — только имя файла, без папок.
                string safeName = Path.GetFileName(name);
                string folder = OtherSchedulesFolder;
                string path = Path.Combine(folder, safeName);
                string fullFolder = Path.GetFullPath(folder);
                string fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase))
                    return new { columns = new string[0], rows = new object[0], error = "Доступ запрещён" };

                return ReadScheduleTable(fullPath);
            }
            catch (Exception ex)
            {
                return new { columns = new string[0], rows = new object[0], error = ex.Message };
            }
        }

        // Версия ReadOtherSchedule с поддержкой нескольких листов.
        private static object ReadOtherScheduleWithSheets(string name, int sheetIdx)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return new { columns = new string[0], rows = new object[0], sheets = new string[0], currentSheet = 0, error = "Имя файла не указано" };

                string safeName = Path.GetFileName(name);
                string folder = OtherSchedulesFolder;
                string path = Path.Combine(folder, safeName);
                string fullFolder = Path.GetFullPath(folder);
                string fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase))
                    return new { columns = new string[0], rows = new object[0], sheets = new string[0], currentSheet = 0, error = "Доступ запрещён" };

                return ReadScheduleWithSheets(fullPath, sheetIdx);
            }
            catch (Exception ex)
            {
                return new { columns = new string[0], rows = new object[0], sheets = new string[0], currentSheet = 0, error = ex.Message };
            }
        }

        // === Столовая: кэшированные xlsx-меню по дате ===
        // Файлы могут иметь разные имена:
        //   {date}-sm.xlsx   (старый формат: 2026-01-15-sm.xlsx)
        //   {date}.xlsx       (новый формат: 2026-01-15.xlsx)
        //   sm-{date}.xlsx    (альтернативный)
        // Пробуем по очереди.
        //
        // Настройки расширения canteen берутся из ExtensionStateService:
        //   foodBlockId, sourceUrl, filenameTemplate, autoDownload, refreshIntervalMinutes.
        // Если источник не задан — fallback на cfg.FoodBlockId (старый формат).
        //
        // Шаблон URL: подстановки {id}, {filename}, {yyyy-mm-dd}, {yyyy}, {mm}, {dd}.
        //   Пример: https://foodmonitoring.ru/{id}/food/{filename}
        // Шаблон имени файла: подстановки {yyyy-mm-dd}, {yyyy}, {mm}, {dd}.
        //   По умолчанию: {yyyy-mm-dd}-sm.xlsx
        private static object LoadCanteenMenu(string date)
        {
            try
            {
                // Читаем настройки расширения canteen.
                var extState = InfoKioskApp.Plugins.PluginManager.GetState("canteen");
                var settings = extState.Settings ?? new JObject();

                string foodBlockId = (string?)settings["foodBlockId"] ?? "";
                if (string.IsNullOrWhiteSpace(foodBlockId))
                {
                    // Fallback на старый config.
                    var cfg = ConfigService.LoadConfig();
                    foodBlockId = string.IsNullOrWhiteSpace(cfg.FoodBlockId) ? "15159" : cfg.FoodBlockId;
                }

                string sourceUrlTemplate = (string?)settings["sourceUrl"] ?? "";
                string filenameTemplate = (string?)settings["filenameTemplate"] ?? "{yyyy-mm-dd}-sm.xlsx";
                bool autoDownload = settings["autoDownload"]?.Type == JTokenType.Boolean
                    ? (bool)settings["autoDownload"]
                    : true;

                // Парсим дату (ожидается yyyy-mm-dd).
                DateTime dt;
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out dt))
                {
                    dt = DateTime.Today;
                }
                string yyyy = dt.ToString("yyyy");
                string mm = dt.ToString("MM");
                string dd = dt.ToString("dd");

                // Вычисляем имя файла по шаблону.
                string filename = filenameTemplate
                    .Replace("{yyyy-mm-dd}", date)
                    .Replace("{yyyy}", yyyy)
                    .Replace("{mm}", mm)
                    .Replace("{dd}", dd);

                string safeId = string.Join("_", foodBlockId.Split(Path.GetInvalidFileNameChars()));
                string folder = Path.Combine(DataRoot, "CanteenMenu", safeId);
                Directory.CreateDirectory(folder);

                // Список возможных имён файлов — пробуем по очереди.
                // Сначала точное имя по шаблону, потом старые варианты.
                string[] candidates = {
                    filename,             // имя по шаблону (приоритет)
                    $"{date}-sm.xlsx",    // старый формат
                    $"{date}.xlsx",       // новый формат (yyyy-mm-dd.xlsx)
                    $"sm-{date}.xlsx",    // альтернативный
                    $"{date}.xls",        // старый Excel
                };

                string file = null;
                foreach (var c in candidates)
                {
                    string p = Path.Combine(folder, c);
                    if (File.Exists(p)) { file = p; break; }
                }

                // Если не нашли точное совпадение — поищем любой .xlsx, начинающийся с даты.
                if (file == null && Directory.Exists(folder))
                {
                    try
                    {
                        var match = Directory.GetFiles(folder, $"{date}*.xlsx")
                                              .FirstOrDefault();
                        if (match != null) file = match;
                    }
                    catch { }
                }

                // Если файла нет и включено авто-скачивание — пробуем скачать.
                if (file == null && autoDownload && !string.IsNullOrWhiteSpace(sourceUrlTemplate))
                {
                    file = TryDownloadCanteenMenu(sourceUrlTemplate, foodBlockId, filename, date, yyyy, mm, dd, folder);
                }

                if (file == null)
                    return new { hasMenu = false, message = "На этот день меню не загружено. Загрузите файл с именем " + filename + (autoDownload && !string.IsNullOrWhiteSpace(sourceUrlTemplate) ? " или проверьте URL источника в настройках расширения." : ""), rows = new object[0] };

                using var wb = new ClosedXML.Excel.XLWorkbook(file);
                var ws = wb.Worksheets.First();
                var used = ws.RangeUsed();
                if (used == null)
                    return new { hasMenu = true, rows = new object[0] };

                int colCount = used.ColumnCount();
                int rowCount = used.RowCount();

                var columns = new List<string>();
                for (int c = 1; c <= colCount; c++)
                    columns.Add(ws.Cell(1, c).GetString());

                var rows = new List<List<string>>();
                for (int r = 2; r <= rowCount; r++)
                {
                    var row = new List<string>();
                    for (int c = 1; c <= colCount; c++)
                        row.Add(ws.Cell(r, c).GetString());
                    if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
                        rows.Add(row);
                }

                return new { hasMenu = true, date, columns, rows };
            }
            catch (Exception ex)
            {
                return new { hasMenu = false, error = ex.Message };
            }
        }

        // Пытается скачать файл меню по шаблону URL и сохранить в folder.
        // Шаблон: https://foodmonitoring.ru/{id}/food/{filename}
        // Подстановки: {id}, {filename}, {yyyy-mm-dd}, {yyyy}, {mm}, {dd}.
        // Возвращает путь к скачанному файлу или null при ошибке.
        private static string TryDownloadCanteenMenu(string urlTemplate, string foodBlockId,
            string filename, string date, string yyyy, string mm, string dd, string folder)
        {
            try
            {
                string url = urlTemplate
                    .Replace("{id}", Uri.EscapeDataString(foodBlockId))
                    .Replace("{filename}", Uri.EscapeDataString(filename))
                    .Replace("{yyyy-mm-dd}", Uri.EscapeDataString(date))
                    .Replace("{yyyy}", Uri.EscapeDataString(yyyy))
                    .Replace("{mm}", Uri.EscapeDataString(mm))
                    .Replace("{dd}", Uri.EscapeDataString(dd));

                string targetFile = Path.Combine(folder, filename);
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var resp = client.GetAsync(url).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return null;

                using (var fs = File.Create(targetFile))
                {
                    resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                }
                Console.WriteLine($"[Canteen] downloaded {filename} from {url}");
                return targetFile;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Canteen] download failed: {ex.Message}");
                return null;
            }
        }

        // === Новости (только published) ===
        private static object LoadPublishedNews()
        {
            try
            {
                string path = Path.Combine(DataRoot, "news", "published.json");
                if (!File.Exists(path)) return new List<NewsPost>();
                var json = File.ReadAllText(path);
                return JArray.Parse(json);
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Почётная доска ===
        private static object LoadHonor()
        {
            try
            {
                string path = Path.Combine(DataRoot, "honor", "items.json");
                if (!File.Exists(path)) return new List<HonorPerson>();
                var json = File.ReadAllText(path);
                return JArray.Parse(json);
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Медиа: категории (папки в data/media/) ===
        private static object ListMediaCategories()
        {
            try
            {
                string root = Path.Combine(DataRoot, "media");
                if (!Directory.Exists(root)) return new List<object>();
                var cats = Directory.GetDirectories(root)
                    .Select(d => new
                    {
                        name = Path.GetFileName(d),
                        postsCount = Directory.GetDirectories(d).Length
                    })
                    .OrderBy(c => c.name)
                    .ToList();
                return cats;
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Медиа: посты в категории ===
        private static object ListMediaPosts(string category, int page, int pageSize)
        {
            try
            {
                string catDir = Path.Combine(DataRoot, "media", category);
                if (!Directory.Exists(catDir)) return new { posts = new object[0], total = 0, page, pageSize };

                var posts = new List<object>();
                foreach (var postDir in Directory.GetDirectories(catDir).OrderByDescending(d => File.GetLastWriteTime(d)))
                {
                    string metaPath = Path.Combine(postDir, "post.json");
                    if (!File.Exists(metaPath)) continue;
                    try
                    {
                        var post = JObject.Parse(File.ReadAllText(metaPath));
                        post["id"] = Path.GetFileName(postDir);
                        post["category"] = category;
                        posts.Add(post);
                    }
                    catch { }
                }
                int total = posts.Count;
                var pageItems = posts.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                return new { posts = pageItems, total, page, pageSize };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Медиа: один пост (со списком изображений) ===
        private static object GetMediaPost(string category, string id)
        {
            try
            {
                string postDir = Path.Combine(DataRoot, "media", category, id);
                if (!Directory.Exists(postDir)) return new { error = "Пост не найден" };

                string metaPath = Path.Combine(postDir, "post.json");
                JObject post = File.Exists(metaPath)
                    ? JObject.Parse(File.ReadAllText(metaPath))
                    : new JObject();
                post["id"] = id;
                post["category"] = category;

                var images = Directory.GetFiles(postDir)
                    .Where(f => IsImageExt(Path.GetExtension(f)))
                    .Select(f => Path.GetFileName(f))
                    .ToList();
                post["images"] = new JArray(images);

                return post;
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Документы ===
        private static object ListDocuments()
        {
            try
            {
                string dir = Path.Combine(DataRoot, "documents");
                if (!Directory.Exists(dir)) return new List<object>();
                var files = Directory.GetFiles(dir)
                    .Select(f => new
                    {
                        name = Path.GetFileName(f),
                        size = new FileInfo(f).Length,
                        modified = File.GetLastWriteTime(f),
                        ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant()
                    })
                    .OrderByDescending(f => f.modified)
                    .ToList();
                return files;
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Просмотр XLSX-файлов с навигацией по листам ===
        // Используется для отображения Excel-документов в киоске.
        // Возвращает список имён листов + содержимое выбранного листа.
        private static object ReadXlsxWithSheets(string name, int sheetIdx)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return new { error = "Имя файла не указано" };

                string safeName = Path.GetFileName(name);
                string dir = Path.Combine(DataRoot, "documents");
                string path = Path.Combine(dir, safeName);
                if (!File.Exists(path))
                    return new { error = "Файл не найден: " + safeName };

                using var wb = new ClosedXML.Excel.XLWorkbook(path);
                var sheetNames = wb.Worksheets.Select(w => w.Name).ToList();
                if (sheetNames.Count == 0)
                    return new { error = "В файле нет листов" };

                if (sheetIdx < 0 || sheetIdx >= sheetNames.Count)
                    sheetIdx = 0;

                var ws = wb.Worksheets.ElementAt(sheetIdx);
                var used = ws.RangeUsed();
                var columns = new List<string>();
                var rows = new List<List<string>>();

                if (used != null)
                {
                    int colCount = used.ColumnCount();
                    int rowCount = used.RowCount();
                    for (int c = 1; c <= colCount; c++)
                        columns.Add(ws.Cell(1, c).GetString());
                    for (int r = 2; r <= rowCount; r++)
                    {
                        var row = new List<string>();
                        for (int c = 1; c <= colCount; c++)
                            row.Add(ws.Cell(r, c).GetString());
                        if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
                            rows.Add(row);
                    }
                }

                return new
                {
                    sheets = sheetNames,
                    currentSheet = sheetIdx,
                    columns,
                    rows
                };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Пользовательские разделы: список файлов ===
        private static object ListCustomSectionFiles(string folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath)) return new { files = new object[0] };
                string resolved = Path.IsPathRooted(folderPath)
                    ? folderPath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderPath);
                if (!Directory.Exists(resolved)) return new { files = new object[0] };

                var files = Directory.GetFiles(resolved)
                    .Select(f => new
                    {
                        name = Path.GetFileName(f),
                        size = new FileInfo(f).Length,
                        modified = File.GetLastWriteTime(f),
                        ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant()
                    })
                    .OrderByDescending(f => f.modified)
                    .ToList();
                return new { files };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // === Idle-изображения: сканируем assets/idle/ и data/idle/ ===
        private static object ListIdleImages()
        {
            var result = new List<string>();
            // Источники idle-изображений (только пользовательские, без SVG-заглушек):
            // 1. data/idle/ — пользовательские idle-фото.
            // 2. data/schoolPhotos/ — фото школы (загружаются через админку).
            // 3. data/schoolLogo/ — логотип школы.
            string[] roots =
            {
                Path.Combine(DataRoot, "idle"),
                Path.Combine(DataRoot, "schoolPhotos"),
                Path.Combine(DataRoot, "schoolLogo"),
            };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in Directory.GetFiles(root).OrderBy(f => f))
                {
                    if (IsImageExt(Path.GetExtension(f)))
                    {
                        var name = Path.GetFileName(f);
                        if (root.Contains("schoolPhotos"))
                            result.Add($"/download?target=schoolphotos&name={Uri.EscapeDataString(name)}");
                        else if (root.Contains("schoolLogo"))
                            result.Add($"/download?target=schoollogo&name={Uri.EscapeDataString(name)}");
                        else
                            result.Add($"/download?target=idle&name={Uri.EscapeDataString(name)}");
                    }
                }
            }
            Console.WriteLine($"[Idle] Found {result.Count} images");
            return new { images = result };
        }

        // === URL сборки для скачивания файла ===
        private static object BuildFileUrl(string target, string name, string category, string post)
        {
            if (string.IsNullOrWhiteSpace(name)) return new { url = "" };

            // /download?target=media&category=...&post=...&name=...
            var qs = new List<string> { $"target={Uri.EscapeDataString(target)}", $"name={Uri.EscapeDataString(name)}" };
            if (!string.IsNullOrWhiteSpace(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
            if (!string.IsNullOrWhiteSpace(post)) qs.Add($"post={Uri.EscapeDataString(post)}");
            return new { url = "/download?" + string.Join("&", qs) };
        }

        // === Fullscreen через Win32 ===
        private void SetFullscreen(bool enable)
        {
            var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
            disp?.BeginInvoke(new Action(() =>
            {
                try
                {
                    var window = System.Windows.Application.Current.MainWindow;
                    if (window == null) return;
                    if (enable)
                    {
                        window.WindowStyle = System.Windows.WindowStyle.None;
                        window.WindowState = System.Windows.WindowState.Maximized;
                    }
                    else
                    {
                        window.WindowStyle = System.Windows.WindowStyle.SingleBorderWindow;
                        window.WindowState = System.Windows.WindowState.Normal;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Bridge] SetFullscreen failed: {ex.Message}");
                }
            }));
        }

        // --------------------------- UTILS ---------------------------

        private static bool IsImageExt(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif"
                || ext == ".webp" || ext == ".bmp" || ext == ".svg";
        }

        /// <summary>
        /// JS-хелпер, инжектируемый на каждой странице ДО её скриптов.
        /// Создаёт window.kiosk с методами call() и on().
        /// </summary>
        private const string KioskBridgeScript = @"
(function() {
  if (window.__kioskBridgeInstalled) return;
  window.__kioskBridgeInstalled = true;

  const pending = new Map();
  const listeners = new Map();
  let counter = 0;

  if (!window.chrome || !window.chrome.webview) {
    console.error('[kiosk] chrome.webview is NOT available — bridge will not work');
    return;
  }

  window.chrome.webview.addEventListener('message', (event) => {
    let data = event.data;
    if (typeof data === 'string') {
      try { data = JSON.parse(data); } catch { return; }
    }
    if (!data) return;

    // Ответ на call()
    if (data.id && pending.has(data.id)) {
      const { resolve, reject } = pending.get(data.id);
      pending.delete(data.id);
      if (data.ok === true)  resolve(data.data);
      else                   reject(new Error(data.error || 'unknown error'));
      return;
    }

    // Пуш-событие
    if (data.type && listeners.has(data.type)) {
      for (const cb of listeners.get(data.type)) {
        try { cb(data.data); } catch (e) { console.error('[kiosk.on]', e); }
      }
    }
  });

  window.kiosk = {
    call(type, data, timeoutMs = 15000) {
      return new Promise((resolve, reject) => {
        const id = 'm' + (++counter);
        const t = setTimeout(() => {
          if (pending.has(id)) {
            pending.delete(id);
            reject(new Error('timeout: ' + type));
          }
        }, timeoutMs);
        pending.set(id, {
          resolve: (v) => { clearTimeout(t); resolve(v); },
          reject:  (e) => { clearTimeout(t); reject(e); }
        });
        try {
          window.chrome.webview.postMessage(JSON.stringify({ id, type, data: data || {} }));
        } catch (e) {
          pending.delete(id);
          clearTimeout(t);
          reject(e);
        }
      });
    },
    on(type, cb) {
      if (!listeners.has(type)) listeners.set(type, []);
      listeners.get(type).push(cb);
      return () => {
        const arr = listeners.get(type);
        const i = arr ? arr.indexOf(cb) : -1;
        if (i >= 0) arr.splice(i, 1);
      };
    }
  };

  console.log('[kiosk] bridge installed');
  window.dispatchEvent(new CustomEvent('kiosk:ready'));
})();
";
    }
}
