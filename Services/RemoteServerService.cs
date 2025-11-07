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
            _listener = new HttpListener();
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

        // ---------------------------- MAIN LISTEN LOOP ----------------------------

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

        // ---------------------------- CORE REQUEST HANDLER ----------------------------

        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
            try
            {
                switch (path)
                {
                    case "/list":
                        await HandleList(ctx);
                        break;
                    case "/upload":
                        await HandleUpload(ctx);
                        break;
                    case "/delete":
                        await HandleDelete(ctx);
                        break;
                    case "/calendar/list":
                        await HandleCalendarList(ctx);
                        break;
                    case "/calendar/add":
                        await HandleCalendarAdd(ctx);
                        break;
                    case "/calendar/delete":
                        await HandleCalendarDelete(ctx);
                        break;
                    case "/config":
                        if (ctx.Request.HttpMethod == "GET")
                            await HandleGetConfig(ctx);
                        else if (ctx.Request.HttpMethod == "POST")
                            await HandlePostConfig(ctx);
                        else
                            await WriteText(ctx, "Unsupported method", 405);
                        break;
                    default:
                        {
                            string webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote");
                            string requestPath = ctx.Request.Url.AbsolutePath.TrimStart('/');

                            // Если запрошен корень — отдаем index.html
                            if (string.IsNullOrEmpty(requestPath))
                                requestPath = "index.html";

                            string filePath = Path.Combine(webRoot, requestPath);

                            if (File.Exists(filePath))
                            {
                                try
                                {
                                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                                    string mime;

                                    // C# 7.3: используем обычный if/else, не switch expression
                                    if (ext == ".html" || ext == ".htm")
                                        mime = "text/html; charset=utf-8";
                                    else if (ext == ".js")
                                        mime = "application/javascript";
                                    else if (ext == ".css")
                                        mime = "text/css";
                                    else if (ext == ".png")
                                        mime = "image/png";
                                    else if (ext == ".jpg" || ext == ".jpeg")
                                        mime = "image/jpeg";
                                    else if (ext == ".json")
                                        mime = "application/json";
                                    else if (ext == ".svg")
                                        mime = "image/svg+xml";
                                    else
                                        mime = "application/octet-stream";

                                    ctx.Response.StatusCode = 200;
                                    ctx.Response.ContentType = mime;

                                    // C# 7.3: нет File.ReadAllBytesAsync — читаем синхронно, затем пишем асинхронно
                                    byte[] data = File.ReadAllBytes(filePath);
                                    ctx.Response.ContentLength64 = data.Length;
                                    await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);

                                    ctx.Response.OutputStream.Close();
                                    ctx.Response.Close();
                                }
                                catch (Exception ex)
                                {
                                    await WriteText(ctx, $"Ошибка при отдаче файла: {ex.Message}", 500);
                                }
                            }
                            else
                            {
                                await WriteText(ctx, "404 Not Found", 404);
                            }
                            break;
                        }

                }
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Ошибка: {ex.Message}", 500);
            }
        }

        // ---------------------------- API IMPLEMENTATION ----------------------------

        private static string GetFolderByTarget(string target)
        {
            target = (target ?? "").ToLowerInvariant();
            if (target == "media") return Path.Combine(DataRoot, "media");
            if (target == "docs" || target == "documents") return Path.Combine(DataRoot, "documents");
            if (target == "schedule" || target == "schedules") return Path.Combine(DataRoot, "schedules");
            return DataRoot;
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
                    modified = File.GetLastWriteTimeUtc(f)
                })
                .ToList();

            await WriteJson(ctx, JsonConvert.SerializeObject(new { target, files }, Formatting.Indented));
        }

        private static async Task HandleUpload(HttpListenerContext ctx)
        {
            try
            {
                string target = ctx.Request.QueryString["target"] ?? "media";
                string folder = GetFolderByTarget("schedules"); // всё хранится в одной папке
                Directory.CreateDirectory(folder);

                string fileName = ctx.Request.QueryString["name"] ?? ctx.Request.Headers["X-Filename"];
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = $"file_{DateTime.Now:yyyyMMdd_HHmmss}";

                string path = Path.Combine(folder, fileName);
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    await ctx.Request.InputStream.CopyToAsync(fs);

                Console.WriteLine($"✅ Загружен файл {fileName} → {folder}");

                // === ЛОГИКА ДЛЯ РАСПИСАНИЙ ===
                if (target == "main" || target == "changes" || target == "schedules")
                {
                    var config = ConfigService.LoadConfig();

                    if (target == "main")
                    {
                        config.MainSchedulePath = path;
                        Console.WriteLine($"📘 Установлено основное расписание: {fileName}");
                    }
                    else if (target == "changes")
                    {
                        config.ChangesPath = path;
                        Console.WriteLine($"🗓 Установлено изменённое расписание: {fileName}");
                    }
                    else // schedules = "дополнительные"
                    {
                        if (config.Schedules == null)
                            config.Schedules = new List<AppConfig.ScheduleItem>();

                        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        var existing = config.Schedules.FirstOrDefault(x =>
                            x.Name.Equals(nameWithoutExt, StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            existing.FilePath = path;
                            Console.WriteLine($"♻ Обновлено расписание {nameWithoutExt}");
                        }
                        else
                        {
                            config.Schedules.Add(new AppConfig.ScheduleItem
                            {
                                Name = nameWithoutExt,
                                FilePath = path
                            });
                            Console.WriteLine($"➕ Добавлено новое расписание {nameWithoutExt}");
                        }
                    }

                    ConfigService.SaveConfig(config);
                }

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
            string name = ctx.Request.QueryString["name"];
            if (string.IsNullOrWhiteSpace(name))
            {
                await WriteText(ctx, "Missing file name", 400);
                return;
            }

            string folder = GetFolderByTarget(target);
            string path = Path.Combine(folder, name);

            if (File.Exists(path))
            {
                File.Delete(path);
                Console.WriteLine($"🗑 Удалён файл {name}");
                await WriteJson(ctx, JsonConvert.SerializeObject(new { status = "deleted", file = name }));
            }
            else
            {
                await WriteText(ctx, "File not found", 404);
            }
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
            string title = ctx.Request.QueryString["title"];
            if (string.IsNullOrWhiteSpace(title))
            {
                await WriteText(ctx, "Missing title", 400);
                return;
            }

            var events = CalendarService.LoadEvents() ?? new List<CalendarEvent>();
            int before = events.Count;
            events.RemoveAll(e => e.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
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
    }
}

