using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace InfoKioskApp.Services
{
    public static class RemoteServerService
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly string WebRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote");

        public static bool IsRunning => _listener != null && _listener.IsListening;

        public static void Start(int port = 8080)
        {
            if (IsRunning) return;

            while (!IsPortFree(port))
                port++;

            Directory.CreateDirectory(WebRoot);
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
            _listener.Start();

            Task.Run(() => ListenLoop(_cts.Token));

            Console.WriteLine($"🌐 Remote Admin running on port {port}");
        }

        public static void Stop()
        {
            if (!IsRunning) return;
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }

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

        private static bool IsPortFree(int port)
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpListeners();
            return !tcpConnections.Any(p => p.Port == port);
        }
        private static string ResolveFolder(string folderKey)
        {
            // Маппинг ключей (пополняй при необходимости)
            switch ((folderKey ?? "").ToLowerInvariant())
            {
                case "schedules":
                case "schedule":
                case "main":
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "schedules");
                case "extra":
                case "extras":
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "schedules", "extra");
                case "media":
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "media");
                case "documents":
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "documents");
                case "clubs":
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "clubs");
                default:
                    // по умолчанию — uploads (на всякий случай)
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads");
            }
        }

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
        // helper: выбрать папку по типу (C# 7.3 friendly)
        private static string GetFolderForType(string type)
        {
            type = (type ?? "").ToLowerInvariant();
            if (type == "media")
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "media");
            if (type == "schedule" || type == "schedules")
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "schedules");
            // default -> documents
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "documents");
        }


        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            try
            {
                if (path == "/upload" && ctx.Request.HttpMethod == "POST")
                {
                    string type = ctx.Request.QueryString["type"];
                    string sub = ctx.Request.QueryString["sub"] ?? "";
                    string targetFolder = GetFolderForType(type);

                    // Для расписаний — создаем подпапки
                    if (type == "schedule" || type == "schedules")
                    {
                        if (sub == "main") targetFolder = Path.Combine(targetFolder, "main");
                        else if (sub == "changes") targetFolder = Path.Combine(targetFolder, "changes");
                        else targetFolder = Path.Combine(targetFolder, "others");
                    }

                    Directory.CreateDirectory(targetFolder);

                    string fileName = ctx.Request.Headers["X-Filename"];
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = $"file_{DateTime.Now.Ticks}";
                    string filePath = Path.Combine(targetFolder, fileName);

                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                        await ctx.Request.InputStream.CopyToAsync(fs);

                    // === Если расписание — обновляем конфиг ===
                    if (type == "schedule" || type == "schedules")
                    {
                        var config = ConfigService.LoadConfig();
                        if (sub == "main")
                            config.MainSchedulePath = filePath;
                        else if (sub == "changes")
                            config.ChangesPath = filePath;
                        else
                        {
                            if (config.Schedules == null) config.Schedules = new List<AppConfig.ScheduleItem>();
                            config.Schedules.Add(new AppConfig.ScheduleItem
                            {
                                Name = Path.GetFileNameWithoutExtension(fileName),
                                FilePath = filePath
                            });
                        }
                        ConfigService.SaveConfig(config);
                    }

                    await WriteText(ctx, $"✅ Файл {fileName} загружен в {targetFolder}");
                    return;
                }

                if (path == "/raw" && ctx.Request.HttpMethod == "GET")
                {
                    string type = ctx.Request.QueryString["type"];
                    string name = ctx.Request.QueryString["name"];
                    if (string.IsNullOrEmpty(name))
                    {
                        await WriteText(ctx, "Missing name", 400);
                        return;
                    }

                    string folder = GetFolderForType(type);
                    string filePath = Path.Combine(folder, name);

                    if (!File.Exists(filePath))
                    {
                        await WriteText(ctx, "File not found", 404);
                        return;
                    }

                    // content type
                    string ext = Path.GetExtension(filePath).ToLower();
                    string mime = "application/octet-stream";
                    if (ext == ".pdf") mime = "application/pdf";
                    else if (ext == ".png") mime = "image/png";
                    else if (ext == ".jpg" || ext == ".jpeg") mime = "image/jpeg";
                    else if (ext == ".html") mime = "text/html";
                    else if (ext == ".css") mime = "text/css";
                    else if (ext == ".js") mime = "application/javascript";
                    // add others if needed

                    ctx.Response.ContentType = mime;
                    ctx.Response.ContentLength64 = new FileInfo(filePath).Length;
                    using (var fs = File.OpenRead(filePath))
                    {
                        await fs.CopyToAsync(ctx.Response.OutputStream);
                    }
                    ctx.Response.OutputStream.Close();
                    ctx.Response.Close();
                    return;
                }

                // === получение конфигурации ===
                if (path == "/config" && ctx.Request.HttpMethod == "GET")
                {
                    var config = ConfigService.LoadConfig();
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    await WriteJson(ctx, json);
                    return;
                }

                // === сохранение конфигурации (полная замена) ===
                if (path == "/config" && ctx.Request.HttpMethod == "POST")
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        string body = await reader.ReadToEndAsync();
                        var newConfig = JsonConvert.DeserializeObject<AppConfig>(body);
                        if (newConfig != null)
                            ConfigService.SaveConfig(newConfig);
                    }
                    await WriteText(ctx, "✅ Настройки сохранены");
                    return;
                }

                // === список файлов в папке ===
                if (path == "/files" && ctx.Request.HttpMethod == "GET")
                {
                    try
                    {
                        string type = ctx.Request.QueryString["type"];
                        string folder = GetFolderForType(type);
                        Directory.CreateDirectory(folder);

                        var files = Directory.GetFiles(folder)
                            .Select(f => new {
                                name = Path.GetFileName(f),
                                path = f.Replace(AppDomain.CurrentDomain.BaseDirectory, "").TrimStart(Path.DirectorySeparatorChar),
                                size = new FileInfo(f).Length,
                                modified = File.GetLastWriteTimeUtc(f)
                            })
                            .ToArray();

                        string json = JsonConvert.SerializeObject(files, Formatting.Indented);
                        await WriteJson(ctx, json);
                        return;
                    }
                    catch (Exception ex)
                    {
                        await WriteText(ctx, $"Ошибка: {ex.Message}", 500);
                        return;
                    }
                }


                // === удаление файла ===
                if (path == "/files" && ctx.Request.HttpMethod == "DELETE")
                {
                    string type = ctx.Request.QueryString["type"];
                    string name = ctx.Request.QueryString["name"];
                    if (string.IsNullOrEmpty(name))
                    {
                        await WriteText(ctx, "Missing name", 400);
                        return;
                    }
                    string folder = GetFolderForType(type);
                    string filePath = Path.Combine(folder, name);
                    if (File.Exists(filePath)) File.Delete(filePath);
                    await WriteText(ctx, $"Deleted {name}");
                    return;
                }


                // === загрузка файла в указанную папку ===
                if (path == "/upload" && ctx.Request.HttpMethod == "POST")
                {
                    try
                    {
                        // читаем параметр type из query, например: /upload?type=media
                        string type = ctx.Request.QueryString["type"];
                        string targetFolder = GetFolderForType(type);
                        Directory.CreateDirectory(targetFolder);

                        // поддерживаем заголовок X-Filename
                        string fileName = ctx.Request.Headers["X-Filename"];
                        if (string.IsNullOrWhiteSpace(fileName))
                            fileName = $"file_{DateTime.Now.Ticks}";

                        // если клиент присылает множественные части — здесь мы ожидаем raw body (файл)
                        string filePath = Path.Combine(targetFolder, fileName);

                        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                        {
                            ctx.Request.InputStream.CopyTo(fs);
                        }

                        await WriteText(ctx, $"✅ Файл {fileName} сохранён в {targetFolder}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        await WriteText(ctx, $"Ошибка загрузки: {ex.Message}", 500);
                        return;
                    }
                }



                // === установка расписания (пример: назначить main или добавить extra) ===
                if (path == "/set-schedule" && ctx.Request.HttpMethod == "POST")
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        string body = await reader.ReadToEndAsync();
                        /*
                         Ожидаемый JSON пример:
                         { "action":"setMain", "fileName":"MainSchedule.xlsx", "folder":"schedules" }
                         или
                         { "action":"addExtra", "name":"Вечерние", "fileName":"evening.xlsx", "folder":"schedules/extra" }
                         */
                        dynamic obj = JsonConvert.DeserializeObject(body);
                        if (obj == null) { await WriteText(ctx, "Bad JSON", 400); return; }

                        var config = ConfigService.LoadConfig();

                        string action = (string)(obj.action ?? "");
                        if (action == "setMain")
                        {
                            string fileName = (string)obj.fileName;
                            string folderKey = (string)(obj.folder ?? "schedules");
                            string folder = ResolveFolder(folderKey);
                            string filePath = Path.Combine(folder, fileName);
                            if (File.Exists(filePath))
                            {
                                config.MainSchedulePath = filePath;
                                ConfigService.SaveConfig(config);
                                await WriteText(ctx, $"✅ Main schedule set to {fileName}");
                            }
                            else
                            {
                                await WriteText(ctx, $"Файл не найден: {filePath}", 404);
                            }
                        }
                        else if (action == "addExtra")
                        {
                            string name = (string)obj.name ?? Path.GetFileNameWithoutExtension((string)obj.fileName);
                            string fileName = (string)obj.fileName;
                            string folderKey = (string)(obj.folder ?? "schedules/extra");
                            string folder = ResolveFolder(folderKey);
                            string filePath = Path.Combine(folder, fileName);
                            if (File.Exists(filePath))
                            {
                                // config.Schedules — список кастомных расписаний
                                if (config.Schedules == null) config.Schedules = new List<AppConfig.ScheduleItem>();
                                config.Schedules.Add(new AppConfig.ScheduleItem { Name = name, FilePath = filePath });
                                ConfigService.SaveConfig(config);
                                await WriteText(ctx, $"✅ Added extra schedule {name}");
                            }
                            else
                            {
                                await WriteText(ctx, $"Файл не найден: {filePath}", 404);
                            }
                        }
                        else
                        {
                            await WriteText(ctx, "Unknown action", 400);
                        }
                    }
                    return;
                }

                // индексная страница
                if (path == "/" || path == "/index.html")
                {
                    string indexPath = Path.Combine(WebRoot, "index.html");
                    if (File.Exists(indexPath))
                    {
                        byte[] data = File.ReadAllBytes(indexPath);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.ContentLength64 = data.Length;
                        await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
                    }
                    else
                    {
                        await WriteText(ctx, "index.html не найден", 404);
                    }
                    ctx.Response.OutputStream.Close();
                    ctx.Response.Close();
                    return;
                }


                // если не найдено
                await WriteText(ctx, "404 Not Found", 404);
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Ошибка: {ex.Message}", 500);
            }
        }



    }
}
