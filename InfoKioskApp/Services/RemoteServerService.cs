using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InfoKioskApp.Models;
using System.Runtime.InteropServices;


namespace InfoKioskApp.Services
{
    public static class RemoteServerService
    {
        private static HttpListener _listener ;
        private static CancellationTokenSource _cts;
        public static bool IsRunning => _listener != null && _listener.IsListening;
        public static event Action<bool> ServerStatusChanged;

        private static readonly string DataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

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





        private static async Task HandleStorageInfo(HttpListenerContext ctx)
        {
            try
            {
                var drive = SystemInfoService.GetSystemDrive();
                long dataSize = SystemInfoService.GetDirectorySize(DataRoot);

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
