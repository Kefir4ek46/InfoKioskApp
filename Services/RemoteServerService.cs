using Newtonsoft.Json;
using System;
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

        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimStart('/');

            try
            {
                // === Обработка корня ===
                if (string.IsNullOrEmpty(path))
                    path = "index.html";

                string filePath = Path.Combine(WebRoot, path.Replace('/', Path.DirectorySeparatorChar));

                // === Отдаём статические файлы (HTML, CSS, JS) ===
                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLower();
                    string mime;
                    switch (ext)
                    {
                        case ".html": mime = "text/html"; break;
                        case ".css": mime = "text/css"; break;
                        case ".js": mime = "application/javascript"; break;
                        case ".png": mime = "image/png"; break;
                        case ".jpg":
                        case ".jpeg": mime = "image/jpeg"; break;
                        case ".ico": mime = "image/x-icon"; break;
                        default: mime = "text/plain"; break;
                    }


                    byte[] data = File.ReadAllBytes(filePath);
                    ctx.Response.ContentType = $"{mime}; charset=utf-8";
                    ctx.Response.ContentLength64 = data.Length;
                    await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
                    ctx.Response.OutputStream.Close();
                    return;
                }

                // === Получение конфигурации ===
                if (path == "config" && ctx.Request.HttpMethod == "GET")
                {
                    var config = ConfigService.LoadConfig();
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    await WriteJson(ctx, json);
                    return;
                }

                // === Сохранение конфигурации ===
                if (path == "config" && ctx.Request.HttpMethod == "POST")
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream))
                    {
                        string body = await reader.ReadToEndAsync();
                        var newConfig = JsonConvert.DeserializeObject<AppConfig>(body);
                        ConfigService.SaveConfig(newConfig);
                    }
                    await WriteText(ctx, "✅ Настройки сохранены");
                    return;
                }

                // === Файлы ===
                if (path.StartsWith("files"))
                {
                    string category = ctx.Request.QueryString["cat"] ?? "documents";
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", category);
                    Directory.CreateDirectory(folder);

                    // Получение списка файлов
                    if (ctx.Request.HttpMethod == "GET")
                    {
                        var files = Directory.GetFiles(folder)
                            .Select(f => Path.GetFileName(f))
                            .ToList();
                        await WriteJson(ctx, JsonConvert.SerializeObject(files, Formatting.Indented));
                        return;
                    }

                    // Удаление файла
                    if (ctx.Request.HttpMethod == "DELETE")
                    {
                        var name = ctx.Request.QueryString["name"];
                        if (!string.IsNullOrEmpty(name))
                        {
                            string pathToDelete = Path.Combine(folder, name);
                            if (File.Exists(pathToDelete)) File.Delete(pathToDelete);
                            await WriteText(ctx, $"🗑 Удалён файл {name}");
                            return;
                        }
                    }

                    // Загрузка файла
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        string fileName = ctx.Request.Headers["X-Filename"] ?? $"file_{DateTime.Now.Ticks}";
                        string filePathUpload = Path.Combine(folder, fileName);

                        using (var fs = new FileStream(filePathUpload, FileMode.Create))
                            await ctx.Request.InputStream.CopyToAsync(fs);

                        await WriteText(ctx, $"✅ Файл {fileName} загружен");
                        return;
                    }
                }

                // === Перезапуск приложения ===
                if (path == "restart")
                {
                    await WriteText(ctx, "🔄 Перезапуск...");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
                        Application.Current.Shutdown();
                    });
                    return;
                }

                await WriteText(ctx, "404 Not Found", 404);
            }
            catch (Exception ex)
            {
                await WriteText(ctx, $"Ошибка: {ex.Message}", 500);
            }
        }

        #region === Ответы ===
        private static async Task WriteJson(HttpListenerContext ctx, string json)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }

        private static async Task WriteText(HttpListenerContext ctx, string text, int code = 200)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }
        #endregion
    }
}
