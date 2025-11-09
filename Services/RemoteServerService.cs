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

namespace InfoKioskApp.Services
{
    public static class RemoteServerService
    {
        private static HttpListener _listener;
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
                    _ = Task.Run(() => HandleRequest(ctx));
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
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }

        // ---------------------------- FILE MANAGEMENT ----------------------------

        private static string GetFolderByTarget(string target)
        {
            target = (target ?? "").ToLowerInvariant();
            string schedulesRoot = Path.Combine(DataRoot, "schedules");

            switch (target)
            {
                case "main": return Path.Combine(schedulesRoot, "main");
                case "changes": return Path.Combine(schedulesRoot, "changes");
                case "schedules":
                case "other": return Path.Combine(schedulesRoot, "other");
                case "media": return Path.Combine(DataRoot, "media");
                case "docs":
                case "documents": return Path.Combine(DataRoot, "documents");
                default: return DataRoot;
            }
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
            try
            {
                string target = ctx.Request.QueryString["target"] ?? "media";
                string folder = GetFolderByTarget(target);
                Directory.CreateDirectory(folder);

                // используем универсальный резолвер имени
                string fileName = ResolveFileName(ctx.Request);

                // очистка имени от недопустимых символов
                foreach (char c in Path.GetInvalidFileNameChars())
                    fileName = fileName.Replace(c, '_');

                string path = Path.Combine(folder, fileName);

                // Для main/changes — храним только один файл (удаляем предыдущие)
                if (target == "main" || target == "changes")
                {
                    foreach (var f in Directory.GetFiles(folder))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    await ctx.Request.InputStream.CopyToAsync(fs);

                Console.WriteLine($"✅ Загружен файл {fileName} → {folder}");
                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "ok", name = fileName, savedTo = path }));
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
            var events = CalendarService.LoadEvents() ?? new List<CalendarEvent>();
            string json = JsonConvert.SerializeObject(events, Formatting.Indented);
            await WriteJson(ctx, json);
        }

        private static async Task HandleCalendarAdd(HttpListenerContext ctx)
        {
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                string body = await reader.ReadToEndAsync();
                try
                {
                    var newEvent = JsonConvert.DeserializeObject<CalendarEvent>(body);
                    if (newEvent == null)
                    {
                        await WriteText(ctx, "Invalid JSON", 400);
                        return;
                    }

                    var events = CalendarService.LoadEvents() ?? new List<CalendarEvent>();
                    events.Add(newEvent);
                    CalendarService.SaveEvents(events);

                    await WriteText(ctx, $"✅ Добавлено событие: {newEvent.Title}");
                }
                catch (Exception ex)
                {
                    await WriteText(ctx, $"Ошибка добавления: {ex.Message}", 500);
                }
            }
        }

        private static async Task HandleCalendarDelete(HttpListenerContext ctx)
        {
            string id = ctx.Request.QueryString["id"];
            string title = ctx.Request.QueryString["title"];

            var events = CalendarService.LoadEvents() ?? new List<CalendarEvent>();
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
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
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
        }

        // ---------------------------- HELPERS ----------------------------

        private static async Task WriteJson(HttpListenerContext ctx, string json, int code = 200)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }

        private static async Task WriteText(HttpListenerContext ctx, string text, int code = 200)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
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
