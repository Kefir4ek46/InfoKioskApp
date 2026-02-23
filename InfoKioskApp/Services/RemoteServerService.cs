using InfoKioskApp.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace InfoKioskApp.Services
{
    public static class RemoteServerService
    {
        private static HttpListener _listener ;
        private static CancellationTokenSource _cts;
        public static bool IsRunning => _listener != null && _listener.IsListening;
        public static event Action<bool> ServerStatusChanged;

        private static readonly string DataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        private static readonly HashSet<string> AdminTokens = new();
        private static readonly HashSet<string> EditorTokens = new();
        private static DateTime _lastCpuSampleTimeUtc = DateTime.UtcNow;
        private static TimeSpan _lastCpuTotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;

        private static string NewsRoot => Path.Combine(DataRoot, "news");
        private static string NewsPendingPath => Path.Combine(NewsRoot, "pending.json");
        private static string NewsPublishedPath => Path.Combine(NewsRoot, "published.json");
        private static string NewsRejectedPath => Path.Combine(NewsRoot, "rejected.json");
        private static string NewsEditorsPath => Path.Combine(NewsRoot, "editors.json");

        // ---------------------------- START / STOP ----------------------------

        public static void Start(int port = 8080)
        {
            if (IsRunning) return;

            while (!IsPortFree(port)) port++;

            Directory.CreateDirectory(DataRoot);
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

                    case "/news/published": await HandleNewsPublishedList(ctx); break;
                    case "/news/editor/submit": await HandleEditorSubmitNews(ctx); break;
                    case "/news/editor/mine": await HandleEditorMyNews(ctx); break;

                    case "/news/admin/pending": await HandleAdminPendingNews(ctx); break;
                    case "/news/admin/publish": await HandleAdminPublishNews(ctx); break;
                    case "/news/admin/reject": await HandleAdminRejectNews(ctx); break;
                    case "/news/admin/update": await HandleAdminUpdateNews(ctx); break;
                    case "/news/admin/delete": await HandleAdminDeleteNews(ctx); break;
                    case "/news/admin/editors": await HandleAdminEditors(ctx); break;

                    case "/settings/get": await HandleGetConfig(ctx); break;

                    case "/api/storage":
                        await HandleStorageInfo(ctx);
                        break;


                    case "/download": await HandleDownload(ctx); break;

                    case "/list": await HandleList(ctx); break;
                    case "/upload": await HandleUpload(ctx); break;
                    case "/delete": await HandleDelete(ctx); break;
                    case "/calendar/list": await HandleCalendarList(ctx); break;
                    case "/calendar/add": await HandleCalendarAdd(ctx); break;
                    case "/calendar/delete": await HandleCalendarDelete(ctx); break;
                    case "/config":
                        if (ctx.Request.HttpMethod == "GET")
                            await HandleGetConfig(ctx);
                        else if (ctx.Request.HttpMethod == "POST")
                            await HandlePostConfig(ctx);
                        else
                            await WriteText(ctx, "Unsupported method", 405);
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
                ext == ".png" ? "image/png" :
                ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" :
                ext == ".gif" ? "image/gif" :
                ext == ".webp" ? "image/webp" :
                ext == ".bmp" ? "image/bmp" :
                ext == ".mp4" ? "video/mp4" :
                ext == ".webm" ? "video/webm" :
                ext == ".ogg" || ext == ".ogv" ? "video/ogg" :
                ext == ".mov" ? "video/quicktime" :
                "application/octet-stream";

            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }




        // ---------------------------- STATIC FILES ----------------------------

        private static async Task HandleStaticFiles(HttpListenerContext ctx)
        {
            string webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote");
            string requestPath = ctx.Request.Url.AbsolutePath.TrimStart('/');

            if (string.IsNullOrEmpty(requestPath))
                requestPath = "index.html";

            string filePath = Path.Combine(webRoot, requestPath);

            if (!File.Exists(filePath))
            {
                await WriteText(ctx, "404 Not Found", 404);
                return;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string mime =
                ext == ".html" || ext == ".htm" ? "text/html; charset=utf-8" :
                ext == ".js" ? "application/javascript" :
                ext == ".css" ? "text/css" :
                ext == ".png" ? "image/png" :
                ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" :
                ext == ".json" ? "application/json" :
                "application/octet-stream";

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = mime;
            byte[] data = File.ReadAllBytes(filePath);
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
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

                using (var fs = new FileStream(filePath2, FileMode.Create, FileAccess.Write))
                    await ctx.Request.InputStream.CopyToAsync(fs);

                Console.WriteLine($"✅ Загружен файл {fileName} → {folder}");
                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "ok", name = fileName, savedTo = filePath2 }));
            }
            catch (Exception ex)
            {
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
            return !string.IsNullOrWhiteSpace(token) && AdminTokens.Contains(token);
        }

        private static bool IsEditorAuthorized(HttpListenerContext ctx)
        {
            string token = ctx.Request.Headers["X-Editor-Token"] ?? "";
            return !string.IsNullOrWhiteSpace(token) && EditorTokens.Contains(token);
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
            dynamic data = JsonConvert.DeserializeObject(body);
            string password = (string?)data?.password ?? "";
            string pin = ConfigService.LoadConfig().PinCode ?? "1234";

            if (password != pin)
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            string token = Guid.NewGuid().ToString("N");
            AdminTokens.Add(token);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { token }));
        }

        private static async Task HandleEditorLogin(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                await WriteText(ctx, "Unsupported method", 405);
                return;
            }

            string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(body);
            string login = (string?)data?.login ?? "";
            string password = (string?)data?.password ?? "";

            var editor = ReadEditors().FirstOrDefault(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && e.Password == password && e.Active);
            if (editor == null)
            {
                await WriteText(ctx, "Unauthorized", 401);
                return;
            }

            string token = Guid.NewGuid().ToString("N");
            EditorTokens.Add(token);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { token, login = editor.Login }));
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
            post.Status = "pending";
            post.PhotoFiles ??= new List<string>();
            post.PhotoFiles = post.PhotoFiles
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Path.GetFileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            post.VideoFile = string.IsNullOrWhiteSpace(post.VideoFile) ? null : Path.GetFileName(post.VideoFile);

            var pending = ReadNewsList(NewsPendingPath);
            pending.Add(post);
            WriteNewsList(NewsPendingPath, pending);
            await WriteJson(ctx, JsonConvert.SerializeObject(new { ok = true, id = post.Id }));
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
                    string login = (string?)data?.login ?? "";
                    string password = (string?)data?.password ?? "";
                    if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
                    {
                        await WriteText(ctx, "Bad request", 400);
                        return;
                    }

                    if (editors.Any(e => e.Login.Equals(login, StringComparison.OrdinalIgnoreCase)))
                    {
                        await WriteText(ctx, "Editor exists", 409);
                        return;
                    }

                    editors.Add(new NewsEditor { Login = login, Password = password, Active = true });
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
            }

            await WriteText(ctx, "Unsupported method", 405);
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
