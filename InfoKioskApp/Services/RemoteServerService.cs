using InfoKioskApp.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace InfoKioskApp.Services
{
    public static class RemoteServerService
    {
        private static HttpListener _listener ;
        private static CancellationTokenSource _cts;
        public static bool IsRunning => _listener != null && _listener.IsListening;
        public static int Port { get; private set; } = 8080;
        public static event Action<bool> ServerStatusChanged;

        /// <summary>
        /// Событие, которое срабатывает, когда нужно отправить push-уведомление
        /// в киоск. Подписывается WebMessageBridge (через MainWindow), чтобы
        /// проксировать сообщение в WebView2.
        /// Параметр — объект-сообщение (сериализуется в JSON и отправляется через PostWebMessageAsJson).
        /// </summary>
        public static event Action<object> KioskPushRequested;

        // Корневые папки статики: web/ (киоск) и remote/ (админка).
        // Запросы вида /admin*, /editor*, /shared* обслуживаются из remote/,
        // все остальные — из web/.
        private static readonly string WebRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
        private static readonly string RemoteRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote");

        private static readonly string DataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        // Для хранения токенов используем ConcurrentDictionary вместо HashSet,
        // т.к. HttpListener обрабатывает запросы параллельно (Task.Run в ListenLoop).
        // HashSet<T>.Add/Contains не thread-safe и при высокой нагрузке могут
        // выбрасывать InvalidOperationException или терять данные — это приводило
        // к тому, что admin-логин проходил, но сразу после этого первый же
        // /news/admin/pending получал 401 (токен не находился в HashSet).
        private static readonly ConcurrentDictionary<string, byte> AdminTokens = new();
        private static readonly ConcurrentDictionary<string, byte> EditorTokens = new();
        private static DateTime _lastCpuSampleTimeUtc = DateTime.UtcNow;
        private static TimeSpan _lastCpuTotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;

        private static string NewsRoot => Path.Combine(DataRoot, "news");
        private static string NewsPendingPath => Path.Combine(NewsRoot, "pending.json");
        private static string NewsPublishedPath => Path.Combine(NewsRoot, "published.json");
        private static string NewsRejectedPath => Path.Combine(NewsRoot, "rejected.json");
        private static string NewsEditorsPath => Path.Combine(NewsRoot, "editors.json");
        private static string HonorRoot => Path.Combine(DataRoot, "honor");
        private static string HonorItemsPath => Path.Combine(HonorRoot, "items.json");

        // ---------------------------- START / STOP ----------------------------

        public static void Start(int port = 8080)
        {
            if (IsRunning) return;

            while (!IsPortFree(port)) port++;
            Port = port;

            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(WebRoot);
            Directory.CreateDirectory(RemoteRoot);
            _cts = new CancellationTokenSource();
            _listener = new HttpListener
            {
                IgnoreWriteExceptions = true
            };
            _listener.Prefixes.Add($"http://+:{port}/");
            _listener.Start();

            Task.Run(() => ListenLoop(_cts.Token));

            Console.WriteLine($"🌐 RemoteServerService запущен на порту {port}");
            ServerStatusChanged?.Invoke(true);
        }


        public static void Stop()
        {
            if (!IsRunning) return;
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
            ServerStatusChanged?.Invoke(false);
            Console.WriteLine("🛑 RemoteServerService остановлен");
        }

        private static bool IsPortFree(int port)
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpListeners();
            return !tcpConnections.Any(p => p.Port == port);
        }

        // ---------------------------- MAIN LOOP ----------------------------

        private static async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx), token);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Server Error] {ex.Message}");
                }
            }
        }

        // ---------------------------- ROUTING ----------------------------

        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
            try
            {
                switch (path)
                {
                    case "/media/categories": await HandleMediaCategoriesList(ctx); break;
                    case "/media/category/add": await HandleMediaCategoryAdd(ctx); break;
                    case "/media/category/delete": await HandleMediaCategoryDelete(ctx); break;
                    case "/media/category/rename": await HandleMediaCategoryRename(ctx); break;

                    // в switch(path) добавьте:



                    case "/media/posts": await HandleMediaPostsList(ctx); break; // ?category=&page=&pageSize=
                    case "/media/post": await HandleMediaPostGet(ctx); break;  // ?category=&id=
                    case "/media/post/create": await HandleMediaPostCreate(ctx); break;
                    case "/media/post/delete": await HandleMediaPostDelete(ctx); break;
                    case "/media/post/updateimages": await HandleMediaPostUpdateImages(ctx); break;

                    // updater
                    case "/api/update/check": await HandleUpdateCheck(ctx); break;
                    case "/api/update/install": await HandleUpdateInstall(ctx); break;
                    case "/api/update/log": await HandleUpdateLog(ctx); break;
                    case "/api/update/rollback": await HandleUpdateRollback(ctx); break;
                    case "/api/update/status": await HandleUpdateStatus(ctx); break;

                    




                    case "/auth/admin/login": await HandleAdminLogin(ctx); break;
                    case "/auth/editor/login": await HandleEditorLogin(ctx); break;
                    case "/auth/editor/device-login": await HandleEditorDeviceLogin(ctx); break;
                    case "/auth/editor/change-password": await HandleEditorChangePassword(ctx); break;
                    case "/auth/admin/change-password": await HandleAdminChangePassword(ctx); break;
                    case "/system/volume": await HandleSystemVolume(ctx); break;

                    case "/news/published": await HandleNewsPublishedList(ctx); break;
                    case "/news/editor/submit": await HandleEditorSubmitNews(ctx); break;
                    case "/news/editor/mine": await HandleEditorMyNews(ctx); break;
                    case "/news/editor/update-profile": await HandleEditorUpdateProfile(ctx); break;
                    case "/news/editor/pending": await HandleEditorPendingNews(ctx); break;
                    case "/news/editor/publish": await HandleEditorPublishNews(ctx); break;
                    case "/news/editor/update": await HandleEditorUpdateNews(ctx); break;
                    case "/news/editor/info": await HandleEditorInfo(ctx); break;

                    case "/news/admin/pending": await HandleAdminPendingNews(ctx); break;
                    case "/news/admin/publish": await HandleAdminPublishNews(ctx); break;
                    case "/news/admin/reject": await HandleAdminRejectNews(ctx); break;
                    case "/news/admin/update": await HandleAdminUpdateNews(ctx); break;
                    case "/news/admin/delete": await HandleAdminDeleteNews(ctx); break;
                    case "/news/admin/editors": await HandleAdminEditors(ctx); break;

                    case "/honor/list": await HandleHonorList(ctx); break;
                    case "/honor/save": await HandleHonorSave(ctx); break;
                    case "/honor/delete": await HandleHonorDelete(ctx); break;

                    case "/settings/get": await HandleGetConfig(ctx); break;

                    case "/api/storage":
                        await HandleStorageInfo(ctx);
                        break;

                    // /api/info — агрегированный снимок системы (через SystemInfoService.GetInfo()).
                    // Возвращает machine + memory + disk + process в одном JSON.
                    case "/api/info":
                        await HandleSystemInfo(ctx);
                        break;

                    // Прокси к сайту школы: позволяет обойти X-Frame-Options,
                        // который иначе блокирует <iframe src="https://..."> в киоске.
                    case "/schoolsite/proxy":
                        await HandleSchoolSiteProxy(ctx);
                        break;


                    case "/download": await HandleDownload(ctx); break;

                    case "/list": await HandleList(ctx); break;
                    case "/upload": await HandleUpload(ctx); break;
                    case "/delete": await HandleDelete(ctx); break;
                    case "/calendar/list": await HandleCalendarList(ctx); break;
                    case "/calendar/add": await HandleCalendarAdd(ctx); break;
                    case "/calendar/delete": await HandleCalendarDelete(ctx); break;

                    // === Звонки: чтение/сохранение/активация ===
                    case "/bell/get":
                        await HandleBellGet(ctx);
                        break;
                    case "/bell/save":
                        await HandleBellSave(ctx);
                        break;
                    case "/bell/activate":
                        await HandleBellActivate(ctx);
                        break;

                    case "/config":
                        if (ctx.Request.HttpMethod == "GET")
                            await HandleGetConfig(ctx);
                        else if (ctx.Request.HttpMethod == "POST")
                            await HandlePostConfig(ctx);
                        else
                            await WriteText(ctx, "Unsupported method", 405);
                        break;

                    // === Расширения (плагины) ===
                    case "/extensions/list":
                        await HandleExtensionsList(ctx);
                        break;
                    case "/extensions/toggle":
                        await HandleExtensionsToggle(ctx);
                        break;
                    case "/extensions/save-settings":
                        await HandleExtensionsSaveSettings(ctx);
                        break;
                    case "/extensions/install":
                        await HandleExtensionsInstall(ctx);
                        break;
                    case "/extensions/reload":
                        await HandleExtensionsReload(ctx);
                        break;

                    // === Таблица рекордов (для игр-расширений) ===
                    case "/leaderboard/get":
                        await HandleLeaderboardGet(ctx);
                        break;
                    case "/leaderboard/submit":
                        await HandleLeaderboardSubmit(ctx);
                        break;
                    case "/leaderboard/reset":
                        await HandleLeaderboardReset(ctx);
                        break;
                    default:
                        await HandleStaticFiles(ctx);
                        break;
                }
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Ошибка: {ex.Message}", 500);
            }
        }




        private static async Task HandleDownload(HttpListenerContext ctx)
        {
            string target = ctx.Request.QueryString["target"] ?? "media";
            string name = ctx.Request.QueryString["name"];   // ← ВАЖНО
            string category = ctx.Request.QueryString["category"];
            string post = ctx.Request.QueryString["post"];

            if (string.IsNullOrWhiteSpace(name))
            {
                await WriteText(ctx, "No filename", 400);
                return;
            }

            string folder;

            if (target == "media" && !string.IsNullOrWhiteSpace(category))
            {
                folder = CategoryPath(category);

                if (!string.IsNullOrWhiteSpace(post))
                    folder = Path.Combine(folder, post);     // ← Переход в папку поста
            }
            else if (target == "custom")
            {
                // Кастомный раздел: папку берём из folderPath (абсолютный или относительно BaseDirectory)
                string folderPath = ctx.Request.QueryString["folderPath"];
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    await WriteText(ctx, "Missing folderPath for custom target", 400);
                    return;
                }
                folder = Path.IsPathRooted(folderPath)
                    ? folderPath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderPath);
            }
            else
            {
                folder = GetFolderByTarget(target);
            }

            string filePath = Path.Combine(folder, name);

            if (!File.Exists(filePath))
            {
                await WriteText(ctx, "File not found: " + filePath, 404);
                return;
            }

            byte[] data = File.ReadAllBytes(filePath);
            ctx.Response.StatusCode = 200;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            ctx.Response.ContentType =
                ext == ".png"  ? "image/png" :
                ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" :
                ext == ".gif"  ? "image/gif" :
                ext == ".webp" ? "image/webp" :
                ext == ".bmp"  ? "image/bmp" :
                ext == ".svg"  ? "image/svg+xml" :
                ext == ".mp4"  ? "video/mp4" :
                ext == ".webm" ? "video/webm" :
                ext == ".ogg" || ext == ".ogv" ? "video/ogg" :
                ext == ".mov"  ? "video/quicktime" :
                ext == ".pdf"  ? "application/pdf" :
                ext == ".txt"  ? "text/plain; charset=utf-8" :
                ext == ".html" || ext == ".htm" ? "text/html; charset=utf-8" :
                ext == ".doc" || ext == ".docx" ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" :
                ext == ".xls" || ext == ".xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" :
                ext == ".ppt" || ext == ".pptx" ? "application/vnd.openxmlformats-officedocument.presentationml.presentation" :
                ext == ".json" ? "application/json; charset=utf-8" :
                ext == ".csv"  ? "text/csv; charset=utf-8" :
                "application/octet-stream";

            // Content-Disposition: inline — чтобы браузер показывал файл, а не скачивал.
            // filename параметр нужен для сохранения через «Сохранить как» в просмотрщике.
            string safeName = Uri.EscapeDataString(Path.GetFileName(filePath));
            ctx.Response.Headers["Content-Disposition"] = $"inline; filename=\"{safeName}\"";

            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }




        // ---------------------------- STATIC FILES ----------------------------

        private static async Task HandleStaticFiles(HttpListenerContext ctx)
        {
            string rawPath = ctx.Request.Url.AbsolutePath;
            string requestPath = rawPath.TrimStart('/');

            if (string.IsNullOrEmpty(requestPath))
                requestPath = "index.html";

            // Нормализуем: убираем trailing slash
            string normalizedPath = requestPath.TrimEnd('/');

            // ===== Явные маршруты remote-страниц =====
            //  /remote, /remote/, /remote/index(.html) → remote/index.html
            //  /admin,   /admin/,   /admin/index(.html),   /admin.html   → remote/admin.html
            //  /editor,  /editor/,  /editor/index(.html),  /editor.html  → remote/editor.html
            //  /login,   /login/                              → remote/index.html
            string remoteTarget = null;
            if (normalizedPath.Equals("remote", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("remote/index", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("remote/index.html", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("login", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("login/index", StringComparison.OrdinalIgnoreCase))
            {
                remoteTarget = "index.html";
            }
            else if (normalizedPath.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("admin/", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("admin/index", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("admin/index.html", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("admin.html", StringComparison.OrdinalIgnoreCase))
            {
                remoteTarget = "admin.html";
            }
            else if (normalizedPath.Equals("editor", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("editor/", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("editor/index", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("editor/index.html", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Equals("editor.html", StringComparison.OrdinalIgnoreCase))
            {
                remoteTarget = "editor.html";
            }

            if (remoteTarget != null)
            {
                await ServeStaticFile(ctx, Path.Combine(RemoteRoot, remoteTarget));
                return;
            }

            // ===== Корневой запрос "/" =====
            // Киоск (localhost / 127.0.0.1) → web/index.html
            // Удалённый клиент              → remote/index.html (страница выбора роли)
            if (requestPath == "index.html" || requestPath == "")
            {
                bool isLocal = IsLocalRequest(ctx.Request);
                string rootFile = isLocal
                    ? Path.Combine(WebRoot, "index.html")
                    : Path.Combine(RemoteRoot, "index.html");
                await ServeStaticFile(ctx, rootFile);
                return;
            }

            // ===== Защита киоска от удалённого доступа =====
            // Удалённый клиент (по IP) не должен иметь возможность открывать
            // файлы из web/ — это внутренний UI киоска. Любой запрос к
            // статике web/ (css/js/assets) от удалённого клиента → редирект
            // на страницу выбора роли /remote.
            if (!IsLocalRequest(ctx.Request))
            {
                // Список префиксов, доступных удалённым клиентам.
                // Всё остальное (/, /css, /js, /assets, /index.html, /favicon.ico и т.д.)
                // обслуживается ТОЛЬКО локально.
                bool isRemoteAllowedPath =
                    requestPath.StartsWith("admin",     StringComparison.OrdinalIgnoreCase) ||
                    requestPath.StartsWith("editor",    StringComparison.OrdinalIgnoreCase) ||
                    requestPath.StartsWith("shared",    StringComparison.OrdinalIgnoreCase) ||
                    requestPath.StartsWith("login",     StringComparison.OrdinalIgnoreCase) ||
                    requestPath.StartsWith("remote",    StringComparison.OrdinalIgnoreCase);
                if (!isRemoteAllowedPath)
                {
                    ctx.Response.Redirect("/remote");
                    ctx.Response.Close();
                    return;
                }
            }

            // ===== Общая статика =====
            // Маршрутизация папок:
            //   /admin/*, /editor/*, /shared/*, /login/*  → remote/
            //   /admin.js, /admin.html, /editor.js, /editor.html → remote/
            //     (файлы из корня remote/ — без trailing slash)
            //   /plugins/*                               → plugins/ (локальная папка)
            //   всё остальное                              → web/  (киоск)
            //
            // ВАЖНО: ранее проверка была только "admin/" (с trailing slash),
            // из-за чего /admin.js возвращал 404 (искался в web/, а не в remote/).
            // Это ломало админку: страница /admin грузилась, но admin.js — нет,
            // и обработчик submit формы не привязывался → при вводе PIN форма
            // просто перезагружалась, пароль очищался, ничего не происходило.
            bool isRemote =
                requestPath.StartsWith("admin/", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("admin.", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("editor/", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("editor.", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("shared/", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("login/", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("login.", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("remote/", StringComparison.OrdinalIgnoreCase) ||
                requestPath.StartsWith("remote.", StringComparison.OrdinalIgnoreCase) ||
                // Logo.ico лежит в remote/Logo.ico — обслуживаем оттуда.
                requestPath.Equals("logo.ico", StringComparison.OrdinalIgnoreCase);

            // /plugins/<id>/view.js, /plugins/<id>/view.css, /plugins/<id>/plugin.json —
            // обслуживаем из папки plugins/ рядом с exe. Только локальные запросы
            // (киоск) — удалённым клиентам плагины не видны.
            if (requestPath.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsLocalRequest(ctx.Request))
                {
                    ctx.Response.Redirect("/remote");
                    ctx.Response.Close();
                    return;
                }
                string pluginsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                string pluginRel = requestPath.Substring("plugins/".Length).Replace("\\", "/").TrimStart('/');
                string pluginFile = Path.GetFullPath(Path.Combine(pluginsRoot, pluginRel));
                string pluginsFull = Path.GetFullPath(pluginsRoot);
                if (!pluginFile.StartsWith(pluginsFull, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteText(ctx, "403 Forbidden", 403);
                    return;
                }
                await ServeStaticFile(ctx, pluginFile);
                return;
            }

            string root = isRemote ? RemoteRoot : WebRoot;

            // /remote/* и /shared/* обслуживаются из корня remote/, убираем префикс
            string relPath = requestPath;
            if (isRemote && (relPath.StartsWith("remote/", StringComparison.OrdinalIgnoreCase)))
                relPath = relPath.Substring("remote/".Length);
            if (isRemote && (relPath.StartsWith("shared/", StringComparison.OrdinalIgnoreCase)))
            {
                // shared/ — это папка remote/shared/, оставляем как есть
                root = RemoteRoot;
            }

            // Безопасность: убираем любые .. сегменты
            string safeRel = relPath.Replace("\\", "/").TrimStart('/');
            string filePath = Path.GetFullPath(Path.Combine(root, safeRel));
            string rootFull = Path.GetFullPath(root);
            if (!filePath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                await WriteText(ctx, "403 Forbidden", 403);
                return;
            }

            // Если путь — каталог, ищем index.html
            if (Directory.Exists(filePath))
                filePath = Path.Combine(filePath, "index.html");

            await ServeStaticFile(ctx, filePath);
        }

        private static bool IsLocalRequest(HttpListenerRequest req)
        {
            try
            {
                var ip = req.RemoteEndPoint?.Address;
                if (ip == null) return false;
                return IPAddress.IsLoopback(ip);
            }
            catch { return false; }
        }

        private static async Task ServeStaticFile(HttpListenerContext ctx, string filePath)
        {
            if (!File.Exists(filePath))
            {
                await WriteText(ctx, "404 Not Found: " + Path.GetFileName(filePath), 404);
                return;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string mime =
                ext == ".html" || ext == ".htm" ? "text/html; charset=utf-8" :
                ext == ".js"   || ext == ".mjs" ? "application/javascript; charset=utf-8" :
                ext == ".css"  ? "text/css; charset=utf-8" :
                ext == ".png"  ? "image/png" :
                ext == ".jpg"  || ext == ".jpeg" ? "image/jpeg" :
                ext == ".gif"  ? "image/gif" :
                ext == ".webp" ? "image/webp" :
                ext == ".svg"  ? "image/svg+xml" :
                ext == ".ico"  ? "image/x-icon" :
                ext == ".mp4"  ? "video/mp4" :
                ext == ".webm" ? "video/webm" :
                ext == ".woff" ? "font/woff" :
                ext == ".woff2"? "font/woff2" :
                ext == ".json" ? "application/json; charset=utf-8" :
                ext == ".txt"  ? "text/plain; charset=utf-8" :
                "application/octet-stream";

            // Запрещаем кэширование для HTML/JS/CSS — удобно при разработке
            if (ext == ".html" || ext == ".js" || ext == ".css")
            {
                ctx.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = mime;
            byte[] data = File.ReadAllBytes(filePath);
            ctx.Response.ContentLength64 = data.Length;
            try
            {
                await ctx.Response.OutputStream.WriteAsync(data);
            }
            catch { /* клиент отвалился — не критично */ }
            try { ctx.Response.OutputStream.Close(); } catch { }
            try { ctx.Response.Close(); } catch { }
        }

        // ---------------------------- FILE MANAGEMENT ----------------------------

        private static string GetFolderByTarget(string target)
        {
            target = (target ?? "").ToLowerInvariant();
            string schedulesRoot = Path.Combine(DataRoot, "schedules");

            return target switch
            {
                "main" => Path.Combine(schedulesRoot, "main"),
                "changes" => Path.Combine(schedulesRoot, "changes"),
                "schedules" or "other" => Path.Combine(schedulesRoot, "other"),
                "media" => Path.Combine(DataRoot, "media"),
                "docs" or "documents" => Path.Combine(DataRoot, "documents"),
                "schoollogo" => Path.Combine(DataRoot, "schoolLogo"),
                "schoolphotos" => Path.Combine(DataRoot, "schoolPhotos"),
                "newsmedia" => Path.Combine(NewsRoot, "media"),
                "honor" => Path.Combine(HonorRoot, "media"),
                "idle" => Path.Combine(DataRoot, "idle"),
                _ => DataRoot,
            };
        }

        private static async Task HandleList(HttpListenerContext ctx)
        {
            string target = ctx.Request.QueryString["target"] ?? "media";
            string folder = GetFolderByTarget(target);
            Directory.CreateDirectory(folder);

            var files = Directory.GetFiles(folder)
                .Select(f => new
                {
                    name = Path.GetFileName(f),
                    size = new FileInfo(f).Length,
                    modified = File.GetLastWriteTime(f)
                })
                .OrderByDescending(f => f.modified)
                .ToList();

            await WriteJson(ctx, JsonConvert.SerializeObject(new { target, files }, Formatting.Indented));
        }

        private static async Task HandleUpload(HttpListenerContext ctx)
        {
            string categoryParam = ctx.Request.QueryString["category"];
            string postParam = ctx.Request.QueryString["post"];

            // ---------------------- UPLOAD ДЛЯ ПОСТОВ ----------------------
            if (!string.IsNullOrWhiteSpace(categoryParam) && !string.IsNullOrWhiteSpace(postParam))
            {
                try
                {
                    EnsureCategoryStructure(categoryParam);

                    string postFolder = Path.Combine(CategoryPostsPath(categoryParam), postParam);
                    Directory.CreateDirectory(postFolder);

                    // Определяем имя файла
                    string fileName = ResolveFileName(ctx.Request);
                    foreach (char c in Path.GetInvalidFileNameChars())
                        fileName = fileName.Replace(c, '_');

                    string filePath = Path.Combine(postFolder, fileName);

                    // Сохраняем файл
                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                        await ctx.Request.InputStream.CopyToAsync(fs);

                    // Путь к post.json
                    string postJsonPath = Path.Combine(postFolder, "post.json");
                    if (!File.Exists(postJsonPath))
                    {
                        await WriteText(ctx, "Post JSON not found", 500);
                        return;
                    }

                    // Загружаем и обновляем post.json
                    var postObj = JsonConvert.DeserializeObject<MediaPost>(File.ReadAllText(postJsonPath, Encoding.UTF8));
                    if (postObj.Images == null)
                        postObj.Images = new List<MediaImage>();

                    postObj.Images.Add(new MediaImage
                    {
                        File = fileName,
                        
                    });

                    // Если не было обложки — назначим её
                    if (string.IsNullOrEmpty(postObj.Cover))
                        postObj.Cover = fileName;

                    File.WriteAllText(postJsonPath, JsonConvert.SerializeObject(postObj, Formatting.Indented), Encoding.UTF8);

                    // Обновляем posts.json
                    string idxFile = CategoryPostsIndex(categoryParam);
                    var previews = JsonConvert.DeserializeObject<List<MediaPostPreview>>(File.ReadAllText(idxFile, Encoding.UTF8)) ?? new();

                    var preview = previews.FirstOrDefault(p => p.Id == postParam);
                    if (preview != null)
                    {
                        preview.ImagesCount = postObj.Images.Count;
                        if (string.IsNullOrEmpty(preview.Cover))
                            preview.Cover = fileName;
                    }

                    File.WriteAllText(idxFile, JsonConvert.SerializeObject(previews, Formatting.Indented), Encoding.UTF8);

                    await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "ok", file = fileName }));
                    return;
                }
                catch (Exception ex)
                {
                    await WriteText(ctx, $"Ошибка загрузки поста: {ex.Message}", 500);
                    return;
                }
            }

            // ---------------------- СТАНДАРТНАЯ ЗАГРУЗКА ----------------------
            try
            {
                string target = ctx.Request.QueryString["target"] ?? "media";
                string folder = GetFolderByTarget(target);
                Directory.CreateDirectory(folder);

                string fileName = ResolveFileName(ctx.Request);

                foreach (char c in Path.GetInvalidFileNameChars())
                    fileName = fileName.Replace(c, '_');

                string filePath2 = Path.Combine(folder, fileName);

                // Для main/changes — только один файл
                if (target == "main" || target == "changes")
                {
                    foreach (var f in Directory.GetFiles(folder))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }

                // Сохраняем файл через временный файл + замена (атомарно, безопасно).
                // Прямая запись в filePath2 может падать с SEHException, если файл
                // открыт другим процессом (антивирус, индексатор).
                string tempPath = filePath2 + ".tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                try
                {
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await ctx.Request.InputStream.CopyToAsync(fs);
                    }

                    // Атомарная замена.
                    if (File.Exists(filePath2))
                    {
                        try { File.Delete(filePath2); } catch { }
                    }
                    File.Move(tempPath, filePath2);
                }
                finally
                {
                    // Если временный файл остался (ошибка) — удалим.
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }

                Console.WriteLine($"✅ Загружен файл {fileName} → {folder}");
                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "ok", name = fileName, savedTo = filePath2 }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка загрузки файла: {ex.GetType().Name}: {ex.Message}");
                await WriteText(ctx, $"Ошибка загрузки: {ex.Message}", 500);
            }
        }



        private static async Task HandleDelete(HttpListenerContext ctx)
        {
            string target = ctx.Request.QueryString["target"] ?? "media";

            // имя — пробуем читать безопасно так же, как для загрузки
            string name = ResolveFileName(ctx.Request);

            if (string.IsNullOrWhiteSpace(name))
            {
                await WriteText(ctx, "Missing file name", 400);
                return;
            }

            string folder = GetFolderByTarget(target);
            string path = Path.Combine(folder, name);

            if (!File.Exists(path))
            {
                await WriteText(ctx, "File not found", 404);
                return;
            }

            bool deleted = TryDeleteFile(path);

            if (deleted)
            {
                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "deleted", file = name }));
            }
            else
            {
                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "error", file = name, message = "Файл занят или не удалось удалить." }));
            }

        }

        // ===================== SAFE FILE DELETE =====================

        private static bool TryDeleteFile(string path, int retries = 3, int delayMs = 250)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        Console.WriteLine($"🗑 Удалён файл: {path}");
                    }

                    return true;
                }
                catch (IOException)
                {
                    // файл может быть временно занят просмотром — ждём
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    // тоже может быть при блокировке
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ Ошибка при удалении {path}: {ex.Message}");
                    return false;
                }
            }

            Console.WriteLine($"⚠ Не удалось удалить файл (занят или отсутствует): {path}");
            return false;
        }


        // ---------------------------- CALENDAR API ----------------------------

        private static async Task HandleCalendarList(HttpListenerContext ctx)
        {
            var events = CalendarService.LoadEvents() ?? [];
            string json = JsonConvert.SerializeObject(events, Formatting.Indented);
            await WriteJson(ctx, json);
        }

        private static async Task HandleCalendarAdd(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();
            try
            {
                var newEvent = JsonConvert.DeserializeObject<CalendarEvent>(body);
                if (newEvent == null)
                {
                    await WriteText(ctx, "Invalid JSON", 400);
                    return;
                }

                var events = CalendarService.LoadEvents() ?? [];
                events.Add(newEvent);
                CalendarService.SaveEvents(events);

                await WriteText(ctx, $"✅ Добавлено событие: {newEvent.Title}");
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Ошибка добавления: {ex.Message}", 500);
            }
        }

        private static async Task HandleCalendarDelete(HttpListenerContext ctx)
        {
            string id = ctx.Request.QueryString["id"];
            string title = ctx.Request.QueryString["title"];

            var events = CalendarService.LoadEvents() ?? [];
            int before = events.Count;

            if (!string.IsNullOrEmpty(id))
                events.RemoveAll(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            else if (!string.IsNullOrEmpty(title))
                events.RemoveAll(e => e.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            else
            {
                await WriteText(ctx, "Missing id or title", 400);
                return;
            }

            CalendarService.SaveEvents(events);

            int removed = before - events.Count;
            await WriteJson(ctx, JsonConvert.SerializeObject(new { removed }));
        }


        // ---------------------------- BELL SCHEDULE API ----------------------------
        //
        // Формат BellSchedule.json (новый):
        // {
        //   "active": "default",          // ID активного варианта
        //   "variants": {
        //     "default": {
        //       "name": "Обычный день",
        //       "lessons": [ { "num": 1, "start": "08:30", "end": "09:15" }, ... ]
        //     },
        //     "short": { "name": "Сокращённый", "lessons": [...] }
        //   }
        // }
        //
        // Старый формат (просто { lessons: [...] }) автоматически конвертируется.

        private static string BellSchedulePath => Path.Combine(DataRoot, "BellSchedule.json");

        private static JObject LoadBellScheduleJson()
        {
            try
            {
                if (!File.Exists(BellSchedulePath))
                    return new JObject(
                        new JProperty("active", "default"),
                        new JProperty("variants", new JObject(
                            new JProperty("default", new JObject(
                                new JProperty("name", "Обычный день"),
                                new JProperty("lessons", new JArray())
                            ))
                        ))
                    );
                var json = File.ReadAllText(BellSchedulePath);
                var parsed = JObject.Parse(json);

                // Конвертация старого формата.
                if (parsed["variants"] == null)
                {
                    var lessons = parsed["lessons"] as JArray ?? new JArray();
                    return new JObject(
                        new JProperty("active", "default"),
                        new JProperty("variants", new JObject(
                            new JProperty("default", new JObject(
                                new JProperty("name", "Обычный день"),
                                new JProperty("lessons", lessons)
                            ))
                        ))
                    );
                }
                return parsed;
            }
            catch
            {
                return new JObject(
                    new JProperty("active", "default"),
                    new JProperty("variants", new JObject(
                        new JProperty("default", new JObject(
                            new JProperty("name", "Обычный день"),
                            new JProperty("lessons", new JArray())
                        ))
                    ))
                );
            }
        }

        private static void SaveBellScheduleJson(JObject data)
        {
            Directory.CreateDirectory(DataRoot);
            File.WriteAllText(BellSchedulePath, data.ToString(Formatting.Indented));
        }

        // GET /bell/get — возвращает весь объект { active, variants }
        private static async Task HandleBellGet(HttpListenerContext ctx)
        {
            var data = LoadBellScheduleJson();
            await WriteJson(ctx, data.ToString(Formatting.None));
        }

        // POST /bell/save — сохраняет весь объект { active, variants }
        // Тело: { active: "id", variants: { ... } }
        private static async Task HandleBellSave(HttpListenerContext ctx)
        {
            try
            {
                if (!IsAdminAuthorized(ctx))
                {
                    await WriteText(ctx, "Unauthorized", 401);
                    return;
                }
                string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
                var data = JObject.Parse(body);
                SaveBellScheduleJson(data);
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
            }
            catch (Exception ex)
            {
                await WriteText(ctx, "Ошибка: " + ex.Message, 500);
            }
        }

        // POST /bell/activate — устанавливает активный вариант
        // Тело: { id: "variant_id" }
        private static async Task HandleBellActivate(HttpListenerContext ctx)
        {
            try
            {
                if (!IsAdminAuthorized(ctx))
                {
                    await WriteText(ctx, "Unauthorized", 401);
                    return;
                }
                string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
                var req = JObject.Parse(body);
                string id = (string)req["id"] ?? "default";

                var data = LoadBellScheduleJson();
                var variants = data["variants"] as JObject;
                if (variants == null || variants[id] == null)
                {
                    await WriteText(ctx, "Вариант не найден", 404);
                    return;
                }
                data["active"] = id;
                SaveBellScheduleJson(data);
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, active = id }));
            }
            catch (Exception ex)
            {
                await WriteText(ctx, "Ошибка: " + ex.Message, 500);
            }
        }


        // ---------------------------- EXTENSIONS API ----------------------------
        // Управление расширениями (плагинами) киоска.
        // Состояния хранятся в data/extensions-state.json через ExtensionStateService.

        // GET /extensions/list — список всех расширений с состоянием и настройками.
        // POST /extensions/toggle — { id, enabled } включить/выключить.
        // POST /extensions/save-settings — { id, position, settings } сохранить позицию и настройки.
        // POST /extensions/install — { url } установить из GitHub/zip.

        private static async Task HandleExtensionsList(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            var descriptors = InfoKioskApp.Plugins.PluginManager.GetAdminDescriptors();
            await WriteJson(ctx, JsonConvert.SerializeObject(descriptors, Formatting.Indented));
        }

        private static async Task HandleExtensionsToggle(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }
            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            bool enabled = (bool?)data?.enabled ?? false;
            if (string.IsNullOrEmpty(id))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }
            InfoKioskApp.Plugins.PluginManager.UpdateState(id, enabled, null, null);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, id, enabled }));
        }

        private static async Task HandleExtensionsSaveSettings(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }
            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            string position = (string?)data?.position ?? null;
            JObject settings = data?.settings as JObject;
            bool? showInMore = data?.showInMore != null ? (bool?)data?.showInMore : null;
            bool? blockDuringLesson = data?.blockDuringLesson != null ? (bool?)data?.blockDuringLesson : null;
            if (string.IsNullOrEmpty(id))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }
            InfoKioskApp.Plugins.PluginManager.UpdateState(id, null, position, settings, showInMore, blockDuringLesson);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        // Установка расширения из GitHub-репозитория или .zip-архива.
        // Поддерживаются:
        //   1) https://github.com/user/repo                → скачиваем архив branch master/main
        //   2) https://github.com/user/repo/archive/refs/heads/main.zip
        //   3) https://example.ru/my-extension.zip         → прямой .zip
        //
        // Архив должен содержать plugin.json в корне или в подпапке.
        // Распаковываем в plugins/<id>/ где id берётся из plugin.json.
        private static async Task HandleExtensionsInstall(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }
            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string url = (string?)data?.url ?? "";
            if (string.IsNullOrWhiteSpace(url))
            {
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = "URL не указан" }));
                return;
            }

            try
            {
                // Нормализуем GitHub URL в archive-ссылку.
                string downloadUrl = url;
                if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    // https://github.com/user/repo → https://github.com/user/repo/archive/refs/heads/main.zip
                    // Удаляем возможный trailing slash и .git.
                    string clean = url.TrimEnd('/').Replace("/git", "");
                    if (clean.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                        clean = clean[..^4];
                    if (!clean.Contains("/archive/", StringComparison.OrdinalIgnoreCase))
                    {
                        // По умолчанию main, если не сработает — попробуем master.
                        downloadUrl = clean + "/archive/refs/heads/main.zip";
                    }
                }

                // Скачиваем архив.
                string tempZip = Path.Combine(Path.GetTempPath(), "infokiosk_ext_" + Guid.NewGuid().ToString("N") + ".zip");
                using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                {
                    // Если main.zip не существует (404) — пробуем master.zip.
                    var firstResp = await client.GetAsync(downloadUrl);
                    if (!firstResp.IsSuccessStatusCode && downloadUrl.EndsWith("/main.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = downloadUrl[..^"main.zip".Length] + "master.zip";
                        firstResp = await client.GetAsync(downloadUrl);
                    }
                    if (!firstResp.IsSuccessStatusCode)
                    {
                        await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = $"Не удалось скачать архив (HTTP {firstResp.StatusCode}). Проверьте URL." }));
                        return;
                    }
                    using (var fs = File.Create(tempZip))
                    {
                        await firstResp.Content.CopyToAsync(fs);
                    }
                }

                // Распаковываем во временную папку.
                string tempExtract = Path.Combine(Path.GetTempPath(), "infokiosk_ext_extract_" + Guid.NewGuid().ToString("N"));
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempExtract, overwriteFiles: true);
                File.Delete(tempZip);

                // Ищем plugin.json — либо в корне, либо в единственной подпапке.
                string manifestPath = Path.Combine(tempExtract, "plugin.json");
                string sourceDir = tempExtract;
                if (!File.Exists(manifestPath))
                {
                    // Ищем в подпапках.
                    var subDirs = Directory.GetDirectories(tempExtract);
                    if (subDirs.Length == 1)
                    {
                        manifestPath = Path.Combine(subDirs[0], "plugin.json");
                        sourceDir = subDirs[0];
                    }
                    else
                    {
                        // Глубокий поиск.
                        var found = Directory.GetFiles(tempExtract, "plugin.json", SearchOption.AllDirectories).FirstOrDefault();
                        if (found == null)
                        {
                            Directory.Delete(tempExtract, true);
                            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = "В архиве не найден plugin.json" }));
                            return;
                        }
                        manifestPath = found;
                        sourceDir = Path.GetDirectoryName(found);
                    }
                }

                // Читаем id расширения.
                var manifest = JsonConvert.DeserializeObject<InfoKioskApp.Plugins.JsPluginManifest>(
                    await File.ReadAllTextAsync(manifestPath));
                if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                {
                    Directory.Delete(tempExtract, true);
                    await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = "Некорректный plugin.json (нет id)" }));
                    return;
                }

                // Копируем в plugins/<id>/.
                string pluginsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                string targetDir = Path.Combine(pluginsRoot, manifest.Id);
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                }
                CopyDirectory(sourceDir, targetDir);
                Directory.Delete(tempExtract, true);

                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, id = manifest.Id, name = manifest.Name }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Extensions] install failed: {ex.Message}");
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = "Ошибка установки: " + ex.Message }));
            }
        }

        // Рекурсивное копирование директории.
        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        // POST /extensions/reload — горячая перезагрузка списка расширений.
        // Заново сканирует папку plugins/, обновляет PluginManager._jsPlugins,
        // отправляет push-уведомление в киоск ('data.changed' с section='extensions'),
        // после чего киоск перерисовывает сайдбар с актуальным списком.
        private static async Task HandleExtensionsReload(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            try
            {
                string pluginsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                bool changed = InfoKioskApp.Plugins.PluginManager.ReloadJsPlugins(pluginsRoot);
                Console.WriteLine($"[Extensions] reload: changed={changed}, count={InfoKioskApp.Plugins.PluginManager.JsPlugins.Count}");

                // Отправляем push в киоск — перерисовать сайдбар с новым списком.
                try
                {
                    KioskPushRequested?.Invoke(new { type = "data.changed", data = new { section = "extensions" } });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Extensions] push to kiosk failed: {ex.Message}");
                }

                await WriteJson(ctx, JsonConvert.SerializeObject(new {
                    ok = true,
                    changed,
                    count = InfoKioskApp.Plugins.PluginManager.JsPlugins.Count
                }));
            }
            catch (Exception ex)
            {
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = ex.Message }));
            }
        }


        // ---------------------------- LEADERBOARD API ----------------------------
        // Таблица рекордов для игр-расширений. Хранится в data/leaderboards.json.
        // GET  /leaderboard/get?game=<id>            — топ-N рекордов игры.
        // POST /leaderboard/submit { game, name, score } — добавить рекорд.
        // POST /leaderboard/reset  { game }          — сбросить таблицу игры (админ).

        // GET /leaderboard/get?game=<id>
        // Возвращает { game, entries: [{name, score, date}, ...] } — топ-10.
        // Не требует авторизации (киоск может читать без токена).
        private static async Task HandleLeaderboardGet(HttpListenerContext ctx)
        {
            string game = ctx.Request.QueryString["game"] ?? "";
            if (string.IsNullOrWhiteSpace(game))
            {
                await WriteText(ctx, "Bad request: game required", 400);
                return;
            }
            var entries = InfoKioskApp.Plugins.LeaderboardService.Get(game);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { game, entries }, Formatting.Indented));
        }

        // POST /leaderboard/submit { game, name, score }
        // Добавляет рекорд и возвращает обновлённый топ-10.
        // Не требует авторизации (киоск отправляет рекорд без токена).
        private static async Task HandleLeaderboardSubmit(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }
            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string game = (string?)data?.game ?? "";
            string name = (string?)data?.name ?? "Аноним";
            int score = (int?)data?.score ?? 0;
            if (string.IsNullOrWhiteSpace(game))
            {
                await WriteText(ctx, "Bad request: game required", 400);
                return;
            }
            var entries = InfoKioskApp.Plugins.LeaderboardService.Submit(game, name, score);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, game, entries }, Formatting.Indented));
        }

        // POST /leaderboard/reset { game }
        // Сбрасывает таблицу рекордов одной игры. Требует админ-токен.
        private static async Task HandleLeaderboardReset(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }
            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string game = (string?)data?.game ?? "";
            if (string.IsNullOrWhiteSpace(game))
            {
                await WriteText(ctx, "Bad request: game required", 400);
                return;
            }
            InfoKioskApp.Plugins.LeaderboardService.Reset(game);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, game }));
        }


        // ---------------------------- CONFIG API ----------------------------

        private static async Task HandleGetConfig(HttpListenerContext ctx)
        {
            var config = ConfigService.LoadConfig();
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            await WriteJson(ctx, json);
        }

        private static async Task HandlePostConfig(HttpListenerContext ctx)
        {
            StreamReader streamReader = new(ctx.Request.InputStream, Encoding.UTF8);
            using StreamReader reader = streamReader;
            string body = await reader.ReadToEndAsync();

            try
            {
                var newConfig = JsonConvert.DeserializeObject<AppConfig>(body);
                if (newConfig != null)
                {
                    if (newConfig.Ticker != null)
                    {
                        newConfig.Ticker.Text = (newConfig.Ticker.Text ?? string.Empty).Trim();
                        var cleaned = (newConfig.Ticker.Items ?? new List<string>())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(200)
                            .ToList();
                        newConfig.Ticker.Items = cleaned;

                        if (!newConfig.Ticker.Enabled || (string.IsNullOrWhiteSpace(newConfig.Ticker.Text) && cleaned.Count == 0))
                        {
                            newConfig.Ticker.Text = string.Empty;
                            newConfig.Ticker.Items = new List<string>();
                        }
                    }

                    ConfigService.SaveConfig(newConfig);
                    await WriteText(ctx, "✅ Конфигурация сохранена");
                }
                else
                    await WriteText(ctx, "Bad JSON", 400);
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Ошибка сохранения: {ex.Message}", 500);
            }
        }

        // ---------------------------- NEWS/AUTH API ----------------------------

        private static bool IsAdminAuthorized(HttpListenerContext ctx)
        {
            string token = ctx.Request.Headers["X-Admin-Token"] ?? "";
            return !string.IsNullOrWhiteSpace(token) && AdminTokens.ContainsKey(token);
        }

        private static bool IsEditorAuthorized(HttpListenerContext ctx)
        {
            string token = ctx.Request.Headers["X-Editor-Token"] ?? "";
            return !string.IsNullOrWhiteSpace(token) && EditorTokens.ContainsKey(token);
        }

        private static List<NewsPost> ReadNewsList(string path)
        {
            try
            {
                Directory.CreateDirectory(NewsRoot);
                if (!File.Exists(path)) return new List<NewsPost>();
                return JsonConvert.DeserializeObject<List<NewsPost>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<NewsPost>();
            }
            catch
            {
                return new List<NewsPost>();
            }
        }

        private static void WriteNewsList(string path, List<NewsPost> list)
        {
            Directory.CreateDirectory(NewsRoot);
            File.WriteAllText(path, JsonConvert.SerializeObject(list, Formatting.Indented), Encoding.UTF8);
        }

        private static List<NewsEditor> ReadEditors()
        {
            try
            {
                Directory.CreateDirectory(NewsRoot);
                if (!File.Exists(NewsEditorsPath)) return new List<NewsEditor>();
                return JsonConvert.DeserializeObject<List<NewsEditor>>(File.ReadAllText(NewsEditorsPath, Encoding.UTF8)) ?? new List<NewsEditor>();
            }
            catch
            {
                return new List<NewsEditor>();
            }
        }

        private static void WriteEditors(List<NewsEditor> editors)
        {
            Directory.CreateDirectory(NewsRoot);
            File.WriteAllText(NewsEditorsPath, JsonConvert.SerializeObject(editors, Formatting.Indented), Encoding.UTF8);
        }

        private static async Task HandleAdminLogin(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            string password = "";
            try
            {
                var data = JObject.Parse(body);
                // Принимаем и password, и pin — для совместимости с разными клиентами
                password = (string)data?["password"] ?? (string)data?["pin"] ?? "";
            }
            catch { /* пустое тело или невалидный JSON → password="" */ }

            // Загружаем PIN из конфига. Если файла config.json нет — по умолчанию "1234".
            string pin;
            try
            {
                pin = ConfigService.LoadConfig()?.PinCode ?? "1234";
                if (string.IsNullOrEmpty(pin)) pin = "1234";
            }
            catch
            {
                pin = "1234";
            }

            Console.WriteLine($"[AdminLogin] entered='{password}' (len={password?.Length ?? 0}), expected='{pin}' (len={pin?.Length ?? 0})");

            if (string.IsNullOrEmpty(password) || password != pin)
            {
                // ВАЖНО: передаём код 401 в WriteJson, а не устанавливаем StatusCode после —
                // иначе ответ уже закрыт и StatusCode=401 игнорируется (клиент видит 200).
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = "Неверный PIN-код" }), 401);
                return;
            }

            string token = Guid.NewGuid().ToString("N");
            AdminTokens[token] = 0;
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, token }));
        }

        private static async Task HandleEditorLogin(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            string login = "";
            string password = "";
            string deviceId = "";
            try
            {
                var data = JObject.Parse(body);
                login    = (string)data?["login"]    ?? "";
                password = (string)data?["password"] ?? "";
                deviceId = (string)data?["deviceId"] ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
            {
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = "Логин и пароль обязательны" }), 400);
                return;
            }

            var editors = ReadEditors();
            var editor = editors.FirstOrDefault(e =>
                e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) &&
                e.Password == password && e.Active);
            if (editor == null)
            {
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = false, error = "Неверный логин или пароль" }), 401);
                return;
            }

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                editor.TrustedDeviceId = deviceId;
                WriteEditors(editors);
            }

            string token = Guid.NewGuid().ToString("N");
            EditorTokens[token] = 0;
            await WriteJson(ctx, JsonConvert.SerializeObject(new {
                ok = true,
                token,
                login = editor.Login,
                name = GetEditorDisplayName(editor),
                canPublishWithoutApproval = editor.CanPublishWithoutApproval,
                displayName = editor.DisplayName
            }));
        }

        private static async Task HandleEditorDeviceLogin(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string login = (string?)data?.login ?? "";
            string deviceId = (string?)data?.deviceId ?? "";
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(deviceId))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            var editor = ReadEditors().FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && e.Active && (e.TrustedDeviceId ?? "") == deviceId);
            if (editor == null)
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            string token = Guid.NewGuid().ToString("N");
            EditorTokens[token] = 0;
            await WriteJson(ctx, JsonConvert.SerializeObject(new {
                token,
                login = editor.Login,
                name = GetEditorDisplayName(editor),
                canPublishWithoutApproval = editor.CanPublishWithoutApproval,
                displayName = editor.DisplayName
            }));
        }

        // Возвращает отображаемое имя редактора:
        //   - если задано DisplayName — оно
        //   - иначе Name (задаётся администратором)
        //   - иначе Login
        private static string GetEditorDisplayName(NewsEditor editor)
        {
            if (editor == null) return "";
            string dn = (editor.DisplayName ?? "").Trim();
            if (!string.IsNullOrEmpty(dn)) return dn;
            return string.IsNullOrEmpty(editor.Name) ? editor.Login : editor.Name;
        }

        // /news/editor/info?login=...
        // Возвращает информацию о текущем редакторе (имя, права).
        private static async Task HandleEditorInfo(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            string login = ctx.Request.QueryString["login"] ?? "";
            var editor = ReadEditors().FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
            if (editor == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }
            await WriteJson(ctx, JsonConvert.SerializeObject(new {
                login = editor.Login,
                name = editor.Name,
                displayName = editor.DisplayName ?? "",
                displayAs = GetEditorDisplayName(editor),
                canPublishWithoutApproval = editor.CanPublishWithoutApproval,
                active = editor.Active
            }));
        }

        // /news/editor/update-profile  POST { login, displayName }
        // Редактор меняет своё отображаемое имя.
        private static async Task HandleEditorUpdateProfile(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }
            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string login = (string?)data?.login ?? "";
            string displayName = ((string?)data?.displayName ?? "").Trim();

            var editors = ReadEditors();
            var editor = editors.FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && e.Active);
            if (editor == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }

            // Пустое displayName = сброс к имени администратора.
            editor.DisplayName = string.IsNullOrEmpty(displayName) ? null : displayName;
            WriteEditors(editors);
            await WriteJson(ctx, JsonConvert.SerializeObject(new {
                ok = true,
                displayAs = GetEditorDisplayName(editor),
                displayName = editor.DisplayName
            }));
        }

        // /news/editor/pending  GET  ?login=...
        // Возвращает список новостей на модерации для редакторов с правами публикации.
        // Только для редакторов с CanPublishWithoutApproval = true.
        private static async Task HandleEditorPendingNews(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            string login = ctx.Request.QueryString["login"] ?? "";
            var editor = ReadEditors().FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && e.Active);
            if (editor == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }
            if (!editor.CanPublishWithoutApproval)
            {
                await WriteJson(ctx, JsonConvert.SerializeObject(new { error = "Недостаточно прав", items = new List<NewsPost>() }));
                return;
            }

            // Возвращаем все pending новости, кроме своих собственных
            // (свои собственные автор публикует автоматически при submit).
            var pending = ReadNewsList(NewsPendingPath)
                .Where(x => !(x.AuthorLogin ?? "").Equals(login, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
            await WriteJson(ctx, JsonConvert.SerializeObject(pending, Formatting.Indented));
        }

        // /news/editor/publish  POST { id, login }
        // Редактор с правами публикует новость другого редактора.
        private static async Task HandleEditorPublishNews(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }
            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            string login = (string?)data?.login ?? "";

            var editors = ReadEditors();
            var editor = editors.FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && e.Active);
            if (editor == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }
            if (!editor.CanPublishWithoutApproval)
            {
                await WriteText(ctx, "Forbidden", 403);
                return;
            }

            var pending = ReadNewsList(NewsPendingPath);
            var item = pending.FirstOrDefault(x => x.Id == id);
            if (item == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }

            pending.Remove(item);
            WriteNewsList(NewsPendingPath, pending);

            var published = ReadNewsList(NewsPublishedPath);
            item.Status = "published";
            item.ModeratedAt = DateTime.Now;
            // Записываем, кто опубликовал (если это не автор).
            if (!string.IsNullOrEmpty(item.AuthorLogin) &&
                !item.AuthorLogin.Equals(login, StringComparison.OrdinalIgnoreCase))
            {
                item.PublishedByLogin = login;
                item.PublishedByName = GetEditorDisplayName(editor);
            }
            published.Add(item);
            WriteNewsList(NewsPublishedPath, published);

            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        // /news/editor/update  POST { id, login, title, content, linkUrl }
        // Редактор редактирует свою pending-новость либо (если есть права)
        // pending-новость другого редактора перед публикацией.
        private static async Task HandleEditorUpdateNews(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            string login = (string?)data?.login ?? "";
            string title = ((string?)data?.title ?? "").Trim();
            string content = ((string?)data?.content ?? "").Trim();
            string linkUrl = ((string?)data?.linkUrl ?? "").Trim();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(login))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                await WriteText(ctx, "Title required", 400);
                return;
            }

            var editors = ReadEditors();
            var editor = editors.FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && e.Active);
            if (editor == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }

            var pending = ReadNewsList(NewsPendingPath);
            var item = pending.FirstOrDefault(x => x.Id == id);
            if (item == null)
            {
                await WriteText(ctx, "Not found or already published", 404);
                return;
            }

            // Автор может редактировать свою новость.
            // Редактор с правами может редактировать чужую.
            bool isOwner = (item.AuthorLogin ?? "").Equals(login, StringComparison.OrdinalIgnoreCase);
            if (!isOwner && !editor.CanPublishWithoutApproval)
            {
                await WriteText(ctx, "Forbidden", 403);
                return;
            }

            item.Title = title;
            item.Content = content;
            item.LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl;
            WriteNewsList(NewsPendingPath, pending);

            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        private static async Task HandleAdminChangePassword(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string oldPassword = ((string?)data?.oldPassword ?? "").Trim();
            string newPassword = ((string?)data?.newPassword ?? "").Trim();
            var cfg = ConfigService.LoadConfig();
            if ((cfg.PinCode ?? "1234") != oldPassword)
            {
                await WriteText(ctx, "Old password mismatch", 400);
                return;
            }
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            {
                await WriteText(ctx, "New password too short", 400);
                return;
            }
            cfg.PinCode = newPassword;
            ConfigService.SaveConfig(cfg);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        private static void SendVolumeKey(byte key)
        {
            keybd_event(key, 0, 0, 0);
            keybd_event(key, 0, KEYEVENTF_KEYUP, 0);
        }

        private static async Task HandleSystemVolume(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            string action = (ctx.Request.QueryString["action"] ?? "").ToLowerInvariant();
            switch (action)
            {
                case "up":
                    SendVolumeKey(VK_VOLUME_UP);
                    break;
                case "down":
                    SendVolumeKey(VK_VOLUME_DOWN);
                    break;
                case "mute":
                    SendVolumeKey(VK_VOLUME_MUTE);
                    break;
                default:
                    await WriteText(ctx, "Bad action", 400);
                    return;
            }

            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, action }));
        }

        private static async Task HandleEditorChangePassword(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string login = ((string?)data?.login ?? "").Trim();
            string oldPassword = ((string?)data?.oldPassword ?? "").Trim();
            string newPassword = ((string?)data?.newPassword ?? "").Trim();

            var editors = ReadEditors();
            var editor = editors.FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && e.Active);
            if (editor == null || editor.Password != oldPassword)
            {
                await WriteText(ctx, "Invalid credentials", 400);
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            {
                await WriteText(ctx, "New password too short", 400);
                return;
            }

            editor.Password = newPassword;
            WriteEditors(editors);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        private static async Task HandleNewsPublishedList(HttpListenerContext ctx)
        {
            var list = ReadNewsList(NewsPublishedPath)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
            await WriteJson(ctx, JsonConvert.SerializeObject(list, Formatting.Indented));
        }

        private static async Task HandleEditorSubmitNews(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            var post = JsonConvert.DeserializeObject<NewsPost>(body);
            if (post == null || string.IsNullOrWhiteSpace(post.Title))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            post.Id = Guid.NewGuid().ToString("N");
            post.CreatedAt = DateTime.Now;
            post.PhotoFiles ??= new List<string>();
            post.PhotoFiles = post.PhotoFiles
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Path.GetFileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            post.VideoFile = string.IsNullOrWhiteSpace(post.VideoFile) ? null : Path.GetFileName(post.VideoFile);
            post.LinkUrl = string.IsNullOrWhiteSpace(post.LinkUrl) ? null : post.LinkUrl.Trim();
            post.AuthorName = (post.AuthorName ?? "").Trim();

            // Проверяем права автора на публикацию без модерации.
            var editor = ReadEditors().FirstOrDefault(e =>
                e.Login.Equals(post.AuthorLogin ?? "", StringComparison.OrdinalIgnoreCase) && e.Active);
            bool canAutoPublish = editor != null && editor.CanPublishWithoutApproval;

            if (canAutoPublish)
            {
                // Автопубликация: сразу в published, минуя модерацию.
                post.Status = "published";
                post.ModeratedAt = DateTime.Now;
                post.AutoPublished = true;
                // Автор сам опубликовал — PublishedBy* не заполняем (он же автор).
                var published = ReadNewsList(NewsPublishedPath);
                published.Add(post);
                WriteNewsList(NewsPublishedPath, published);
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, id = post.Id, autoPublished = true }));
            }
            else
            {
                post.Status = "pending";
                var pending = ReadNewsList(NewsPendingPath);
                pending.Add(post);
                WriteNewsList(NewsPendingPath, pending);
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, id = post.Id, autoPublished = false }));
            }
        }

        private static async Task HandleEditorMyNews(HttpListenerContext ctx)
        {
            if (!IsEditorAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            string login = ctx.Request.QueryString["login"] ?? "";
            if (string.IsNullOrWhiteSpace(login))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            var pending = ReadNewsList(NewsPendingPath)
                .Where(x => (x.AuthorLogin ?? "").Equals(login, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            var published = ReadNewsList(NewsPublishedPath)
                .Where(x => (x.AuthorLogin ?? "").Equals(login, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            var rejected = ReadNewsList(NewsRejectedPath)
                .Where(x => (x.AuthorLogin ?? "").Equals(login, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            await WriteJson(ctx, JsonConvert.SerializeObject(new
            {
                pending,
                published,
                rejected
            }, Formatting.Indented));
        }

        private static async Task HandleAdminPendingNews(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            var pending = ReadNewsList(NewsPendingPath)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
            await WriteJson(ctx, JsonConvert.SerializeObject(pending, Formatting.Indented));
        }

        private static async Task HandleAdminPublishNews(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            if (string.IsNullOrWhiteSpace(id))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            var pending = ReadNewsList(NewsPendingPath);
            var item = pending.FirstOrDefault(x => x.Id == id);
            if (item == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }

            pending.Remove(item);
            WriteNewsList(NewsPendingPath, pending);

            var published = ReadNewsList(NewsPublishedPath);
            item.Status = "published";
            item.ModeratedAt = DateTime.Now;
            // Администратор публикует — записываем, что это сделал админ
            // (если только администратор не является автором, чего не бывает).
            if (string.IsNullOrEmpty(item.PublishedByLogin))
            {
                item.PublishedByLogin = "admin";
                item.PublishedByName = "Администратор";
            }
            published.Add(item);
            WriteNewsList(NewsPublishedPath, published);

            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        private static async Task HandleAdminRejectNews(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            string reason = ((string?)data?.reason ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reason)) reason = "Причина не указана";

            var pending = ReadNewsList(NewsPendingPath);
            var item = pending.FirstOrDefault(x => x.Id == id);
            if (item == null)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }

            pending.Remove(item);
            WriteNewsList(NewsPendingPath, pending);

            var rejected = ReadNewsList(NewsRejectedPath);
            item.Status = "rejected";
            item.RejectReason = reason;
            item.ModeratedAt = DateTime.Now;
            rejected.Add(item);
            WriteNewsList(NewsRejectedPath, rejected);

            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        private static async Task HandleAdminUpdateNews(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            string title = ((string?)data?.title ?? "").Trim();
            string content = ((string?)data?.content ?? "").Trim();
            string linkUrl = ((string?)data?.linkUrl ?? "").Trim();

            if (string.IsNullOrWhiteSpace(id))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            foreach (var path in new[] { NewsPendingPath, NewsPublishedPath, NewsRejectedPath })
            {
                var list = ReadNewsList(path);
                var item = list.FirstOrDefault(x => x.Id == id);
                if (item == null) continue;

                if (!string.IsNullOrWhiteSpace(title)) item.Title = title;
                if (!string.IsNullOrWhiteSpace(content)) item.Content = content;
                item.LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl;
                WriteNewsList(path, list);
                await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
                return;
            }

            await WriteText(ctx, "Not found", 404);
        }

        private static async Task HandleAdminDeleteNews(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";

            if (string.IsNullOrWhiteSpace(id))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            bool removed = false;
            foreach (var path in new[] { NewsPendingPath, NewsPublishedPath, NewsRejectedPath })
            {
                var list = ReadNewsList(path);
                int before = list.Count;
                list.RemoveAll(x => x.Id == id);
                if (list.Count != before)
                {
                    WriteNewsList(path, list);
                    removed = true;
                    break;
                }
            }

            if (!removed)
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }

            CleanupUnusedNewsMediaFiles();
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        private static async Task HandleAdminEditors(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod == "GET")
            {
                var editors = ReadEditors();
                await WriteJson(ctx, JsonConvert.SerializeObject(editors, Formatting.Indented));
                return;
            }

            if (ctx.Request.HttpMethod == "POST")
            {
                string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(body);
                string action = (string?)data?.action ?? "add";

                var editors = ReadEditors();

                if (action == "add")
                {
                    string name = ((string?)data?.name ?? "").Trim();
                    string login = (string?)data?.login ?? "";
                    string password = (string?)data?.password ?? "";
                    bool canPublish = (bool?)data?.canPublishWithoutApproval ?? false;
                    if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(name))
                    {
                        await WriteText(ctx, "Bad request", 400);
                        return;
                    }

                    if (editors.Any(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase)))
                    {
                        await WriteText(ctx, "Editor exists", 409);
                        return;
                    }

                    editors.Add(new NewsEditor {
                        Name = name,
                        Login = login,
                        Password = password,
                        Active = true,
                        CanPublishWithoutApproval = canPublish
                    });
                    WriteEditors(editors);
                    await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
                    return;
                }

                if (action == "delete")
                {
                    string login = (string?)data?.login ?? "";
                    editors.RemoveAll(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                    WriteEditors(editors);
                    await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
                    return;
                }

                // Переключение прав на публикацию без модерации (значок короны).
                if (action == "toggle-publish-rights")
                {
                    string login = (string?)data?.login ?? "";
                    var ed = editors.FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                    if (ed == null)
                    {
                        await WriteText(ctx, "Not found", 404);
                        return;
                    }
                    bool current = (bool?)data?.canPublishWithoutApproval ?? !ed.CanPublishWithoutApproval;
                    ed.CanPublishWithoutApproval = current;
                    WriteEditors(editors);
                    await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, canPublishWithoutApproval = ed.CanPublishWithoutApproval }));
                    return;
                }

                // Редактирование имени/пароля редактора администратором.
                if (action == "update")
                {
                    string login = (string?)data?.login ?? "";
                    var ed = editors.FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                    if (ed == null)
                    {
                        await WriteText(ctx, "Not found", 404);
                        return;
                    }
                    string newName = ((string?)data?.name ?? "").Trim();
                    string newPassword = (string?)data?.password ?? "";
                    bool canPublish = (bool?)data?.canPublishWithoutApproval ?? ed.CanPublishWithoutApproval;
                    bool active = (bool?)data?.active ?? ed.Active;
                    if (!string.IsNullOrEmpty(newName)) ed.Name = newName;
                    if (!string.IsNullOrEmpty(newPassword)) ed.Password = newPassword;
                    ed.CanPublishWithoutApproval = canPublish;
                    ed.Active = active;
                    WriteEditors(editors);
                    await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
                    return;
                }
            }

            await WriteText(ctx, "Unsupported method", 405);
        }

        private static List<HonorPerson> ReadHonorItems()
        {
            try
            {
                Directory.CreateDirectory(HonorRoot);
                if (!File.Exists(HonorItemsPath)) return new List<HonorPerson>();
                return JsonConvert.DeserializeObject<List<HonorPerson>>(File.ReadAllText(HonorItemsPath, Encoding.UTF8)) ?? new List<HonorPerson>();
            }
            catch
            {
                return new List<HonorPerson>();
            }
        }

        private static void WriteHonorItems(List<HonorPerson> items)
        {
            Directory.CreateDirectory(HonorRoot);
            File.WriteAllText(HonorItemsPath, JsonConvert.SerializeObject(items, Formatting.Indented), Encoding.UTF8);
        }

        private static async Task HandleHonorList(HttpListenerContext ctx)
        {
            var items = ReadHonorItems().OrderByDescending(x => x.CreatedAt).ToList();
            await WriteJson(ctx, JsonConvert.SerializeObject(items, Formatting.Indented));
        }

        private static async Task HandleHonorSave(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            var item = JsonConvert.DeserializeObject<HonorPerson>(body);
            if (item == null || string.IsNullOrWhiteSpace(item.FullName))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            item.PhotoFile = Path.GetFileName(item.PhotoFile ?? "");
            item.Description ??= "";

            var items = ReadHonorItems();
            var existing = items.FirstOrDefault(x => x.Id == item.Id && !string.IsNullOrWhiteSpace(item.Id));
            if (existing == null)
            {
                item.Id = Guid.NewGuid().ToString("N");
                item.CreatedAt = DateTime.Now;
                items.Add(item);
            }
            else
            {
                existing.FullName = item.FullName;
                existing.Description = item.Description;
                if (!string.IsNullOrWhiteSpace(item.PhotoFile))
                    existing.PhotoFile = item.PhotoFile;
            }

            WriteHonorItems(items);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        private static async Task HandleHonorDelete(HttpListenerContext ctx)
        {
            if (!IsAdminAuthorized(ctx))
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string id = (string?)data?.id ?? "";
            if (string.IsNullOrWhiteSpace(id))
            {
                await WriteText(ctx, "Bad request", 400);
                return;
            }

            var items = ReadHonorItems();
            items.RemoveAll(x => x.Id == id);
            WriteHonorItems(items);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true }));
        }

        // ---------------------------- HELPERS ----------------------------

        private static async Task WriteJson(HttpListenerContext ctx, string json, int code = 200)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }

        private static async Task WriteText(HttpListenerContext ctx, string text, int code = 200)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }
        // helper: безопасно получить корректное имя файла из запроса или заголовка
        private static string ResolveFileName(HttpListenerRequest req)
        {
            // 1) сначала проверяем заголовок Base64 (самый надёжный метод)
            string base64Header = req.Headers["X-Filename-Base64"];
            if (!string.IsNullOrWhiteSpace(base64Header))
            {
                try
                {
                    var bytes = Convert.FromBase64String(base64Header);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    // fallthrough to other methods
                }
            }

            // 2) затем проверяем заголовок X-Filename (percent-encoded or raw)
            string header = req.Headers["X-Filename"];
            if (!string.IsNullOrWhiteSpace(header))
            {
                // header may be percent-encoded (encodeURIComponent) — try unescape
                try
                {
                    string decoded = Uri.UnescapeDataString(header);
                    // if decoded looks like mojibake (contains many high-ASCII characters), try fix
                    if (LooksLikeMojibake(decoded))
                    {
                        var bytes = decoded.Select(c => (byte)c).ToArray();
                        try { return Encoding.UTF8.GetString(bytes); } catch { /* ignore */ }
                    }
                    return decoded;
                }
                catch { /* ignore and fallback */ }
            }

            // 3) finally try querystring name
            string q = req.QueryString["name"];
            if (!string.IsNullOrWhiteSpace(q))
            {
                try
                {
                    string decoded = Uri.UnescapeDataString(q);
                    if (LooksLikeMojibake(decoded))
                    {
                        var bytes = decoded.Select(c => (byte)c).ToArray();
                        try { return Encoding.UTF8.GetString(bytes); } catch { /* ignore */ }
                    }
                    return decoded;
                }
                catch { /* ignore */ }
            }

            // 4) fallback: generate unique name
            return $"file_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        // paths helpers
        private static string MediaRoot => Path.Combine(DataRoot, "media");
        private static string CategoryPath(string category) => Path.Combine(MediaRoot, category ?? "uncategorized");
        static string CategoryPostsPath(string category) => CategoryPath(category);
        private static string CategoryPostsIndex(string category) => Path.Combine(CategoryPath(category), "posts.json");

        private static void EnsureCategoryStructure(string category)
        {
            var cat = CategoryPath(category);
            Directory.CreateDirectory(cat);
            Directory.CreateDirectory(CategoryPostsPath(category));
            if (!File.Exists(CategoryPostsIndex(category)))
                File.WriteAllText(CategoryPostsIndex(category), "[]", Encoding.UTF8);
        }

        private static async Task HandleMediaCategoriesList(HttpListenerContext ctx)
        {
            Directory.CreateDirectory(MediaRoot);
            string catFile = Path.Combine(MediaRoot, "categories.json");
            if (!File.Exists(catFile))
            {
                File.WriteAllText(catFile, "[]", Encoding.UTF8);
            }
            string json = File.ReadAllText(catFile, Encoding.UTF8);
            await WriteJson(ctx, json);
        }

        private static async Task HandleMediaCategoryAdd(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();

            try
            {
                var obj = JsonConvert.DeserializeObject<dynamic>(body);
                string id = (string)obj.id;
                string name = (string)obj.name;

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    await WriteText(ctx, "Bad JSON", 400);
                    return;
                }

                Directory.CreateDirectory(MediaRoot);
                string catFile = Path.Combine(MediaRoot, "categories.json");
                if (!File.Exists(catFile)) File.WriteAllText(catFile, "[]", Encoding.UTF8);

                var cats = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(catFile, Encoding.UTF8));

                // не добавляем дубликаты
                if (!cats.Any(c => (string)c.id == id))
                {
                    cats.Add(new { id, name });
                    File.WriteAllText(catFile, JsonConvert.SerializeObject(cats, Formatting.Indented), Encoding.UTF8);
                }

                EnsureCategoryStructure(id);

                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "ok", id, name }));
            }
            catch (Exception ex)
            {
                await WriteText(ctx, "Error: " + ex.Message, 500);
            }
        }

        private static async Task HandleMediaPostUpdateImages(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();

            try
            {
                var update = JsonConvert.DeserializeObject<MediaPost>(body);
                if (update == null)
                {
                    await WriteText(ctx, "Invalid JSON", 400);
                    return;
                }

                string category = update.Category;
                string id = update.Id;

                if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(id))
                {
                    await WriteText(ctx, "Missing category or id", 400);
                    return;
                }

                string postFolder = Path.Combine(CategoryPostsPath(category), id);
                string postFile = Path.Combine(postFolder, "post.json");

                if (!File.Exists(postFile))
                {
                    await WriteText(ctx, "Post not found", 404);
                    return;
                }

                // читаем текущий пост
                var post = JsonConvert.DeserializeObject<MediaPost>(File.ReadAllText(postFile, Encoding.UTF8));
                post.Images = update.Images ?? new List<MediaImage>();

                // cover = первая картинка
                if (post.Images.Count > 0)
                    post.Cover = post.Images[0].File;

                // сохраняем post.json
                File.WriteAllText(postFile, JsonConvert.SerializeObject(post, Formatting.Indented), Encoding.UTF8);

                // обновляем posts.json (превью)
                string idxFile = CategoryPostsIndex(category);
                var previews = JsonConvert.DeserializeObject<List<MediaPostPreview>>(File.ReadAllText(idxFile, Encoding.UTF8));

                var preview = previews.FirstOrDefault(p => p.Id == id);
                if (preview != null)
                {
                    preview.ImagesCount = post.Images.Count;
                    preview.Cover = post.Cover;
                }

                File.WriteAllText(idxFile, JsonConvert.SerializeObject(previews, Formatting.Indented), Encoding.UTF8);

                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "ok" }));
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Ошибка: {ex.Message}", 500);
            }
        }

        private static async Task HandleMediaCategoryRename(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();

            try
            {
                var obj = JsonConvert.DeserializeObject<dynamic>(body);
                string oldId = (string)obj.oldId;
                string newId = (string)obj.newId;
                string newName = (string)obj.newName;

                if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId) || string.IsNullOrWhiteSpace(newName))
                {
                    await WriteText(ctx, "Bad JSON", 400);
                    return;
                }

                string catFile = Path.Combine(MediaRoot, "categories.json");
                if (!File.Exists(catFile))
                {
                    await WriteText(ctx, "Categories not found", 404);
                    return;
                }

                // Загружаем текущие категории
                var cats = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(catFile, Encoding.UTF8));

                var item = cats.FirstOrDefault(c => (string)c.id == oldId);
                if (item == null)
                {
                    await WriteText(ctx, "Category not found", 404);
                    return;
                }

                // Обновляем данные категории
                item.id = newId;
                item.name = newName;

                // Сохраняем обновлённый categories.json
                File.WriteAllText(catFile, JsonConvert.SerializeObject(cats, Formatting.Indented), Encoding.UTF8);

                // Переименовываем папку
                string oldFolder = CategoryPath(oldId);
                string newFolder = CategoryPath(newId);

                if (Directory.Exists(oldFolder))
                {
                    Directory.Move(oldFolder, newFolder);
                }

                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "renamed", oldId, newId, newName }));
            }
            catch (Exception ex)
            {
                await WriteText(ctx, "Error: " + ex.Message, 500);
            }
        }



        private static async Task HandleMediaCategoryDelete(HttpListenerContext ctx)
        {
            string id = ctx.Request.QueryString["id"];
            if (string.IsNullOrWhiteSpace(id))
            {
                await WriteText(ctx, "Missing id", 400);
                return;
            }

            string catFile = Path.Combine(MediaRoot, "categories.json");
            if (!File.Exists(catFile))
            {
                await WriteText(ctx, "Not found", 404);
                return;
            }

            var cats = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(catFile, Encoding.UTF8));
            cats.RemoveAll(c => (string)c.id == id);

            File.WriteAllText(catFile, JsonConvert.SerializeObject(cats, Formatting.Indented), Encoding.UTF8);

            // Полностью удаляем папку категории
            var folder = CategoryPath(id);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }

            await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "deleted", id }));
        }


        private static async Task HandleMediaPostsList(HttpListenerContext ctx)
        {
            string category = ctx.Request.QueryString["category"] ?? "default";
            int page = int.TryParse(ctx.Request.QueryString["page"], out var p) ? Math.Max(1, p) : 1;
            int pageSize = int.TryParse(ctx.Request.QueryString["pageSize"], out var ps) ? Math.Max(1, ps) : 9;

            EnsureCategoryStructure(category);
            string idxFile = CategoryPostsIndex(category);
            var posts = JsonConvert.DeserializeObject<List<MediaPostPreview>>(File.ReadAllText(idxFile, Encoding.UTF8)) ?? [];

            int total = posts.Count;
            var pageItems = posts.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            await WriteJson(ctx, JsonConvert.SerializeObject(new
            {
                category,
                page,
                pageSize,
                total,
                items = pageItems
            }, Formatting.Indented));
        }

        private static async Task HandleMediaPostGet(HttpListenerContext ctx)
        {
            string id = ctx.Request.QueryString["id"];
            string category = ctx.Request.QueryString["category"] ?? "default";
            if (string.IsNullOrWhiteSpace(id)) { await WriteText(ctx, "Missing id", 400); return; }

            string postFolder = Path.Combine(CategoryPostsPath(category), id);
            string postFile = Path.Combine(postFolder, "post.json");
            if (!File.Exists(postFile)) { await WriteText(ctx, "Not found", 404); return; }

            string json = File.ReadAllText(postFile, Encoding.UTF8);
            await WriteJson(ctx, json);
        }

        private static async Task HandleMediaPostCreate(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();
            try
            {
                var obj = JsonConvert.DeserializeObject<dynamic>(body);
                string category = ((string)(obj.category ?? "default")).ToLowerInvariant();
                string title = (string)obj.title ?? "Без названия";
                string description = (string)obj.description ?? "";
                string date = (string)obj.date ?? DateTime.Now.ToString("yyyy-MM-dd");

                EnsureCategoryStructure(category);

                string postId = $"post_{DateTime.Now:yyyyMMdd_HHmmss}";
                string postFolder = Path.Combine(CategoryPostsPath(category), postId);
                Directory.CreateDirectory(postFolder);

                var post = new MediaPost
                {
                    Id = postId,
                    Category = category,
                    Title = title,
                    Description = description,
                    Date = date,
                    Cover = "",
                    Images = new List<MediaImage>()
                };



                string postJson = JsonConvert.SerializeObject(post, Formatting.Indented);
                File.WriteAllText(Path.Combine(postFolder, "post.json"),
    JsonConvert.SerializeObject(post, Formatting.Indented),
    Encoding.UTF8);


                // update posts index (preview)
                string idxFile = CategoryPostsIndex(category);
                List<MediaPostPreview> posts = JsonConvert.DeserializeObject<List<MediaPostPreview>>(File.ReadAllText(idxFile, Encoding.UTF8)) ?? [];
                posts.Insert(0, new MediaPostPreview
                {
                    Id = postId,
                    Title = title,
                    Date = date,
                    Cover = "",
                    ImagesCount = 0
                });

                File.WriteAllText(idxFile, JsonConvert.SerializeObject(posts, Formatting.Indented), Encoding.UTF8);

                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "ok", id = postId, folder = postFolder }));
            }
            catch (Exception ex)
            {
                await WriteText(ctx, "Error: " + ex.Message, 500);
            }
        }

        private static async Task HandleMediaPostDelete(HttpListenerContext ctx)
        {
            string id = ctx.Request.QueryString["id"];
            string category = ctx.Request.QueryString["category"] ?? "default";
            if (string.IsNullOrWhiteSpace(id)) { await WriteText(ctx, "Missing id", 400); return; }
            string postFolder = Path.Combine(CategoryPostsPath(category), id);
            if (Directory.Exists(postFolder)) { Directory.Delete(postFolder, true); }
            // remove from posts.json
            string idxFile = CategoryPostsIndex(category);
            List<MediaPostPreview> posts = JsonConvert.DeserializeObject<List<MediaPostPreview>>(File.ReadAllText(idxFile, Encoding.UTF8)) ?? [];
            posts.RemoveAll(p => p.Id == id);
            File.WriteAllText(idxFile, JsonConvert.SerializeObject(posts, Formatting.Indented), Encoding.UTF8);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "deleted", id }));
        }

        // -------------UPDATER--------------
        private static async Task HandleUpdateCheck(HttpListenerContext ctx)
        {
            string result = await RunUpdater("check");
            await WriteJson(ctx, result);
        }

        private static async Task HandleUpdateInstall(HttpListenerContext ctx)
        {
            string result = await RunUpdater("update");
            await WriteJson(ctx, result);
        }

        private static async Task HandleUpdateLog(HttpListenerContext ctx)
        {
            string logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "updater.log"
            );

            if (!File.Exists(logPath))
            {
                await WriteText(ctx, "Log not found", 404);
                return;
            }

            string log = File.ReadAllText(logPath, Encoding.UTF8);
            await WriteText(ctx, log);
        }


        private static async Task<string> RunUpdater(string args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exePath = Path.Combine(baseDir, "InfoKioskUpdater.exe");

            if (!File.Exists(exePath))
            {
                return JsonConvert.SerializeObject(new
                {
                    ok = false,
                    action = args,
                    message = "Updater not found",
                    data = new { baseDir, exePath }
                });
            }

            NormalizeUpdaterConfig(baseDir);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = baseDir
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                return JsonConvert.SerializeObject(new
                {
                    ok = false,
                    action = args,
                    message = "Failed to start updater",
                    data = new { baseDir, exePath }
                });
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(error))
            {
                return JsonConvert.SerializeObject(new
                {
                    ok = false,
                    action = args,
                    message = error,
                    data = new { baseDir, exePath }
                });
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return JsonConvert.SerializeObject(new
                {
                    ok = true,
                    action = args,
                    message = "Updater finished",
                    data = new { baseDir, exePath }
                });
            }

            return output;
        }

        private static void NormalizeUpdaterConfig(string baseDir)
        {
            try
            {
                string cfgPath = Path.Combine(baseDir, "updater.config.json");
                if (!File.Exists(cfgPath)) return;

                var obj = JObject.Parse(File.ReadAllText(cfgPath, Encoding.UTF8));
                string expectedInstallDir = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string currentInstallDir = (obj["InstallDir"]?.ToString() ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!string.Equals(expectedInstallDir, currentInstallDir, StringComparison.OrdinalIgnoreCase))
                {
                    obj["InstallDir"] = expectedInstallDir;
                    File.WriteAllText(cfgPath, obj.ToString(Formatting.Indented), Encoding.UTF8);
                }
            }
            catch
            {
                // ignore config normalization errors, updater can still attempt with current values
            }
        }

private static async Task HandleUpdateRollback(HttpListenerContext ctx)
        {
            string result = await RunUpdater("rollback");
            await WriteJson(ctx, result);
        }


        private static async Task<object?> FetchGithubReleaseInfo(string baseDir)
        {
            try
            {
                string updaterConfigPath = Path.Combine(baseDir, "updater.config.json");
                if (!File.Exists(updaterConfigPath)) return null;

                var cfg = JObject.Parse(File.ReadAllText(updaterConfigPath, Encoding.UTF8));
                string repoApiUrl = cfg["RepoApiUrl"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(repoApiUrl)) return null;

                using var client = new WebClient();
                client.Headers.Add("User-Agent", "InfoKioskApp/2.0");
                client.Encoding = Encoding.UTF8;
                string json = await client.DownloadStringTaskAsync(repoApiUrl);
                var rel = JObject.Parse(json);

                return new
                {
                    name = rel["name"]?.ToString() ?? "",
                    tag = rel["tag_name"]?.ToString() ?? "",
                    publishedAt = rel["published_at"]?.ToString() ?? "",
                    url = rel["html_url"]?.ToString() ?? "",
                    body = rel["body"]?.ToString() ?? ""
                };
            }
            catch
            {
                return null;
            }
        }

        private static async Task HandleUpdateStatus(HttpListenerContext ctx)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var githubRelease = await FetchGithubReleaseInfo(baseDir);

                string currentVersionPath = Path.Combine(baseDir, "version.txt");
                string latestVersionPath = Path.Combine(baseDir, "latest_version.txt");
                string updaterExePath = Path.Combine(baseDir, "InfoKioskUpdater.exe");
                string updaterConfigPath = Path.Combine(baseDir, "updater.config.json");
                string updaterLogPath = Path.Combine(baseDir, "updater.log");

                string currentVersion = File.Exists(currentVersionPath)
                    ? File.ReadAllText(currentVersionPath).Trim()
                    : "0.0.0";

                string latestVersion = File.Exists(latestVersionPath)
                    ? File.ReadAllText(latestVersionPath).Trim()
                    : "";

                bool updateAvailable =
                    !string.IsNullOrEmpty(latestVersion) &&
                    latestVersion != currentVersion;

                string installDirFromConfig = "";
                if (File.Exists(updaterConfigPath))
                {
                    try
                    {
                        var cfg = JObject.Parse(File.ReadAllText(updaterConfigPath, Encoding.UTF8));
                        installDirFromConfig = cfg["InstallDir"]?.ToString() ?? "";
                    }
                    catch { }
                }

                var payload = new
                {
                    currentVersion,
                    latestVersion,
                    updateAvailable,
                    paths = new
                    {
                        appBaseDir = baseDir,
                        updaterExePath,
                        updaterConfigPath,
                        updaterLogPath,
                        currentVersionPath,
                        latestVersionPath,
                        installDirFromConfig
                    },
                    githubRelease
                };

                string json = JsonConvert.SerializeObject(payload);

                await WriteJson(ctx, json);
            }
            catch (Exception ex)
            {
                string errorJson = JsonConvert.SerializeObject(new
                {
                    error = ex.Message
                });

                await WriteJson(ctx, errorJson, 500);
            }
        }




        // simple DTO for list (to keep posts.json lightweight)
        class MediaPostPreview
        {
            internal string cover;
            internal string date;

            public required string Id { get; set; }
            public required string Title { get; set; }
            public required string Date { get; set; }
            public required string Cover { get; set; } // filename of first image
            public int ImagesCount { get; set; }
        }






        private static void CleanupUnusedNewsMediaFiles()
        {
            try
            {
                string mediaDir = Path.Combine(NewsRoot, "media");
                if (!Directory.Exists(mediaDir)) return;

                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var listPath in new[] { NewsPendingPath, NewsPublishedPath, NewsRejectedPath })
                {
                    foreach (var post in ReadNewsList(listPath))
                    {
                        if (!string.IsNullOrWhiteSpace(post.VideoFile))
                            used.Add(Path.GetFileName(post.VideoFile));

                        foreach (var photo in post.PhotoFiles ?? new List<string>())
                        {
                            if (!string.IsNullOrWhiteSpace(photo))
                                used.Add(Path.GetFileName(photo));
                        }
                    }
                }

                foreach (var file in Directory.EnumerateFiles(mediaDir))
                {
                    var name = Path.GetFileName(file);
                    if (!used.Contains(name))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        private static object GetPerformanceSnapshot()
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                var gcInfo = GC.GetGCMemoryInfo();
                long totalAvailable = gcInfo.TotalAvailableMemoryBytes > 0 ? gcInfo.TotalAvailableMemoryBytes : 0;
                long usedManaged = GC.GetTotalMemory(false);
                double cpuPercent = ReadProcessCpuUsagePercent(proc);

                return new
                {
                    processWorkingSetBytes = proc.WorkingSet64,
                    processWorkingSet = SystemInfoService.FormatBytes(proc.WorkingSet64),
                    managedMemoryBytes = usedManaged,
                    managedMemory = SystemInfoService.FormatBytes(usedManaged),
                    systemMemoryAvailableBytes = totalAvailable,
                    systemMemoryAvailable = totalAvailable > 0 ? SystemInfoService.FormatBytes(totalAvailable) : "—",
                    cpuUsagePercent = Math.Round(cpuPercent, 1)
                };
            }
            catch
            {
                return new { };
            }
        }

        private static double ReadProcessCpuUsagePercent(Process proc)
        {
            try
            {
                var now = DateTime.UtcNow;
                var total = proc.TotalProcessorTime;
                var elapsedMs = (now - _lastCpuSampleTimeUtc).TotalMilliseconds;
                if (elapsedMs <= 0)
                {
                    _lastCpuSampleTimeUtc = now;
                    _lastCpuTotalProcessorTime = total;
                    return 0;
                }

                var cpuMs = (total - _lastCpuTotalProcessorTime).TotalMilliseconds;
                _lastCpuSampleTimeUtc = now;
                _lastCpuTotalProcessorTime = total;

                double usage = cpuMs / (Environment.ProcessorCount * elapsedMs) * 100.0;
                if (double.IsNaN(usage) || double.IsInfinity(usage)) return 0;
                return Math.Max(0, Math.Min(100, usage));
            }
            catch
            {
                return 0;
            }
        }

        // === Прокси к сайту школы ===
        // Берёт URL из ?url= или из config.SchoolSiteUrl (если задан),
        // делает HTTP-запрос сервером, и отдаёт контент в <iframe> киоска.
        // Это обходит X-Frame-Options: DENY/SAMEORIGIN, который иначе ломает
        // iframe на стороне клиента. Удаляем заголовки X-Frame-Options и
        // Content-Security-Policy из ответа, чтобы iframe отрисовался.
        private static async Task HandleSchoolSiteProxy(HttpListenerContext ctx)
        {
            try
            {
                string url = ctx.Request.QueryString["url"];
                if (string.IsNullOrWhiteSpace(url))
                {
                    // По умолчанию берём из конфига (если задано) или хардкод
                    try
                    {
                        url = ConfigService.LoadConfig()?.SchoolSiteUrl;
                    }
                    catch { }
                    if (string.IsNullOrWhiteSpace(url))
                        url = "https://obo-afan.gosuslugi.ru";
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    await WriteText(ctx, "Invalid URL", 400);
                    return;
                }

                // Делаем запрос от имени сервера — с User-Agent как у браузера,
                // иначе некоторые сайты (в т.ч. Госуслуги) возвращают 403.
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Method = "GET";
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                                "(KHTML, like Gecko) Chrome/120.0 Safari/537.36";
                req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                req.AllowAutoRedirect = true;
                req.Timeout = 15000;

                // Cookie контейнер нужен, чтобы сайты сandatory-куки не возвращали 403.
                req.CookieContainer = new System.Net.CookieContainer();

                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var stream = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    byte[] data = ms.ToArray();

                    ctx.Response.StatusCode = (int)resp.StatusCode;
                    string ct = resp.ContentType ?? "text/html; charset=utf-8";
                    ctx.Response.ContentType = ct;

                    // Удаляем заголовки, которые блокируют iframe:
                    //   X-Frame-Options: DENY/SAMEORIGIN
                    //   Content-Security-Policy: frame-ancestors ...
                    ctx.Response.Headers["X-Frame-Options"] = "ALLOWALL";
                    ctx.Response.Headers.Remove("Content-Security-Policy");
                    // Cross-Origin-Opener-Policy / Cross-Origin-Embedder-Policy —
                    // тоже мешают встраиванию в iframe.
                    ctx.Response.Headers.Remove("Cross-Origin-Opener-Policy");
                    ctx.Response.Headers.Remove("Cross-Origin-Embedder-Policy");
                    ctx.Response.Headers.Remove("Cross-Origin-Resource-Policy");

                    // Если это HTML — добавляем <base href="..."> чтобы относительные
                    // ссылки (CSS, JS, картинки) резолвились к оригинальному домену,
                    // а не к нашему /schoolsite/proxy. И инжектируем JS-скрипт, который
                    // перехватывает клики по <a> и сабмиты форм, и направляет их через
                    // прокси — иначе iframe попытается загрузить внешний URL напрямую,
                    // что приведёт к ошибке "отказано в подключении" (X-Frame-Options).
                    if (ct.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            string html = Encoding.UTF8.GetString(data);
                            string schemeHost = uri.Scheme + "://" + uri.Host + (uri.Port != 80 && uri.Port != 443 ? ":" + uri.Port : "");
                            string baseTag = $"<base href=\"{schemeHost}/\">";
                            // Вставляем <base> сразу после <head> или в начало.
                            int headIdx = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                            if (headIdx >= 0)
                            {
                                int closeTag = html.IndexOf('>', headIdx);
                                if (closeTag >= 0)
                                {
                                    html = html.Insert(closeTag + 1, baseTag);
                                }
                            }
                            else
                            {
                                html = baseTag + html;
                            }

                            // Скрипт-перехватчик: все клики по <a href> и сабмиты форм
                            // направляем через /schoolsite/proxy?url=<encoded original>.
                            // Это решает проблему "отказано в подключении" при переходе
                            // по разделам сайта внутри iframe (внешний URL блокируется
                            // X-Frame-Options, если загружать его напрямую).
                            string interceptorScript = @"
<script>
(function() {
  var PROXY = '/schoolsite/proxy?url=';
  function isExternal(u) {
    if (!u) return false;
    if (u.indexOf('javascript:') === 0) return false;
    if (u.charAt(0) === '#') return false;
    if (u.indexOf('/schoolsite/proxy') === 0) return false;
    if (u.indexOf('http://') === 0 || u.indexOf('https://') === 0) return true;
    // Относительные ссылки резолвятся через <base> к внешнему домену
    return true;
  }
  function rewriteUrl(u) {
    if (!u) return u;
    if (u.indexOf('javascript:') === 0) return u;
    if (u.charAt(0) === '#') return u;
    if (u.indexOf('/schoolsite/proxy') === 0) return u;
    // Резолвим через <base>
    var a = document.createElement('a');
    a.href = u;
    var full = a.href;
    if (full.indexOf('http://') === 0 || full.indexOf('https://') === 0) {
      return PROXY + encodeURIComponent(full);
    }
    return u;
  }
  // Перехват кликов по <a> (на capture-фазе, чтобы сработать раньше)
  document.addEventListener('click', function(e) {
    var node = e.target;
    while (node && node.tagName !== 'A') node = node.parentNode;
    if (!node || node.tagName !== 'A') return;
    var href = node.getAttribute('href');
    if (!href) return;
    if (href.indexOf('javascript:') === 0) return;
    if (href.charAt(0) === '#') return;
    // Если target=_blank — открываем в этом же iframe (киоск не должен открывать новые окна)
    if (node.target === '_blank' || node.target === '_top' || node.target === '_parent') {
      node.target = '_self';
    }
    var newHref = rewriteUrl(href);
    if (newHref !== href) {
      e.preventDefault();
      e.stopPropagation();
      // Важно: используем location.replace, чтобы не засорять историю iframe
      try { window.location.replace(newHref); } catch(_) { window.location.href = newHref; }
    }
  }, true);
  // Перехват сабмита формы — переписываем action на прокси
  document.addEventListener('submit', function(e) {
    var form = e.target;
    if (!form || !form.tagName || form.tagName !== 'FORM') return;
    var action = form.getAttribute('action');
    if (!action) {
      // Нет action — форма сабмитится на текущий URL, который уже через прокси
      return;
    }
    var newAction = rewriteUrl(action);
    if (newAction !== action) {
      form.setAttribute('action', newAction);
    }
  }, true);
})();
</script>";

                            // Вставляем скрипт перед </body> (или в конец, если </body> нет)
                            int bodyCloseIdx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                            if (bodyCloseIdx >= 0)
                            {
                                html = html.Insert(bodyCloseIdx, interceptorScript);
                            }
                            else
                            {
                                html = html + interceptorScript;
                            }

                            data = Encoding.UTF8.GetBytes(html);
                        }
                        catch
                        {
                            // Не упадём, если HTML-парсинг не удался — отдадим как есть.
                        }
                    }

                    ctx.Response.ContentLength64 = data.Length;
                    await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
                    ctx.Response.OutputStream.Close();
                    ctx.Response.Close();
                }
            }
            catch (WebException wex)
            {
                Console.WriteLine($"[SchoolSiteProxy] WebException: {wex.Message}");
                await WriteText(ctx, "School site proxy error: " + wex.Message, 502);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SchoolSiteProxy] Error: {ex.Message}");
                await WriteText(ctx, "School site proxy error: " + ex.Message, 500);
            }
        }

        // /api/info — агрегированный снимок системы (через SystemInfoService.GetInfo).
        // Используется админкой для отображения machine + memory + disk + process
        // в одном запросе (вместо отдельных /api/storage и т.п.).
        private static async Task HandleSystemInfo(HttpListenerContext ctx)
        {
            try
            {
                var info = SystemInfoService.GetInfo();
                string json = JsonConvert.SerializeObject(info, Formatting.Indented);
                await WriteJson(ctx, json);
            }
            catch (Exception ex)
            {
                await WriteText(ctx, "System info error: " + ex.Message, 500);
            }
        }

        private static async Task HandleStorageInfo(HttpListenerContext ctx)
        {
            try
            {
                var drive = SystemInfoService.GetSystemDrive();
                long dataSize = SystemInfoService.GetDirectorySize(DataRoot);
                string mediaPath = Path.Combine(DataRoot, "media");
                string newsPath = Path.Combine(DataRoot, "news");
                long mediaSize = Directory.Exists(mediaPath) ? SystemInfoService.GetDirectorySize(mediaPath) : 0;
                long newsSize = Directory.Exists(newsPath) ? SystemInfoService.GetDirectorySize(newsPath) : 0;
                var result = new
                {
                    disk = new
                    {
                        name = drive.Name,
                        totalBytes = drive.TotalSize,
                        usedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                        freeBytes = drive.AvailableFreeSpace,

                        total = SystemInfoService.FormatBytes(drive.TotalSize),
                        used = SystemInfoService.FormatBytes(drive.TotalSize - drive.AvailableFreeSpace),
                        free = SystemInfoService.FormatBytes(drive.AvailableFreeSpace)
                    },
                    dataFolder = new
                    {
                        path = DataRoot,
                        sizeBytes = dataSize,
                        size = SystemInfoService.FormatBytes(dataSize)
                    },
                    mediaFolder = new
                    {
                        path = mediaPath,
                        sizeBytes = mediaSize,
                        size = SystemInfoService.FormatBytes(mediaSize)
                    },
                    newsFolder = new
                    {
                        path = newsPath,
                        sizeBytes = newsSize,
                        size = SystemInfoService.FormatBytes(newsSize)
                    },
                    app = new
                    {
                        baseDir = AppDomain.CurrentDomain.BaseDirectory,
                        process = Process.GetCurrentProcess().ProcessName,
                        pid = Process.GetCurrentProcess().Id
                    },
                    performance = GetPerformanceSnapshot(),
                    timestamp = DateTime.Now
                };

                await WriteJson(ctx, JsonConvert.SerializeObject(result, Formatting.Indented));
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Storage error: {ex.Message}", 500);
            }
        }



        // helper: crude check for mojibake like "Ð" "Ñ" or sequences "Р" etc.
        private static bool LooksLikeMojibake(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int weird = 0;
            foreach (char c in s)
            {
                // count C1/C2 block characters common in mojibake results (e.g. 'Ð', 'Ñ', 'Р', 'С')
                if (c >= 0x00C0 && c <= 0x00FF) weird++;
                // also count 'Р' (U+0420) etc may indicate double-decoded, but we focus on Latin-1 high range
            }
            // if many such chars -> likely mojibake
            return weird * 2 > s.Length; // >50% high-ascii
        }

    }
}
