using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;


namespace InfoKioskApp.Services
{
    public static class RemoteServerService
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly string WebRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote");

        public static bool IsRunning => _listener != null && _listener.IsListening;

        public static void Start()
        {
            if (IsRunning) return;
            int port = 8080;
            while (!IsPortFree(port))
                port++;
            Directory.CreateDirectory(WebRoot);
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
            _listener.Start();

            Task.Run(() => ListenLoop(_cts.Token));
            Console.WriteLine("🌐 Remote Admin running on http://localhost:8080/");
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
            bool isAvailable = true;
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpListeners();
            if (tcpConnections.Any(p => p.Port == port))
                isAvailable = false;
            return isAvailable;
        }
        private static void EnsureUrlAcl(string prefix)
        {
            try
            {
                var user = Environment.UserName;
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"http add urlacl url={prefix} user={user}",
                    Verb = "runas", // требует запуск от имени администратора
                    CreateNoWindow = true,
                    UseShellExecute = true
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось зарегистрировать URLACL: {ex.Message}");
            }
        }
        private static void EnsureFirewallRule(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"InfoKiosk Local Server\" dir=in action=allow protocol=TCP localport={port}",
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось добавить правило брандмауэра: {ex.Message}");
            }
        }
        private static bool IsReachableFromNetwork(string ip, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(ip, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                    return success && client.Connected;
                }
            }
            catch { return false; }
        }


        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;

            try
            {
                if (path == "/" || path == "/index.html")
                {
                    await WriteHtml(ctx, GetHtmlInterface());
                    return;
                }

                if (path == "/config" && ctx.Request.HttpMethod == "GET")
                {
                    var config = ConfigService.LoadConfig();
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    await WriteJson(ctx, json);
                    return;
                }

                if (path == "/config" && ctx.Request.HttpMethod == "POST")
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

                if (path == "/files" && ctx.Request.HttpMethod == "GET")
                {
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads");
                    Directory.CreateDirectory(folder);
                    var files = Directory.GetFiles(folder);
                    await WriteJson(ctx, JsonConvert.SerializeObject(files, Formatting.Indented));
                    return;
                }

                if (path.StartsWith("/files") && ctx.Request.HttpMethod == "DELETE")
                {
                    var query = ctx.Request.QueryString["name"];
                    if (!string.IsNullOrEmpty(query))
                    {
                        string pathToDelete = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads", query);
                        if (File.Exists(pathToDelete)) File.Delete(pathToDelete);
                        await WriteText(ctx, $"🗑 Удалён файл {query}");
                        return;
                    }
                }

                if (path == "/upload" && ctx.Request.HttpMethod == "POST")
                {
                    string uploadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads");
                    Directory.CreateDirectory(uploadDir);

                    string fileName = ctx.Request.Headers["X-Filename"] ?? $"file_{DateTime.Now.Ticks}";
                    string filePath = Path.Combine(uploadDir, fileName);

                    using (var fs = new FileStream(filePath, FileMode.Create))
                        await ctx.Request.InputStream.CopyToAsync(fs);

                    await WriteText(ctx, $"✅ Файл {fileName} загружен");
                    return;
                }

                if (path == "/restart")
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
        private static async Task WriteHtml(HttpListenerContext ctx, string html)
        {
            byte[] data = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }

        private static async Task WriteJson(HttpListenerContext ctx, string json)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
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
        #endregion


        #region === HTML интерфейс ===
        private static string GetHtmlInterface()
        {
            return @"
<!DOCTYPE html>
<html lang='ru'>
<head>
<meta charset='UTF-8'>
<title>InfoKiosk Remote Admin</title>
<style>
body {
  background:#1e1e1e; color:white;
  font-family:Segoe UI, sans-serif;
  padding:20px;
}
.tabs button {
  padding:10px 20px;
  background:#333;
  color:white;
  border:none;
  cursor:pointer;
  margin-right:8px;
  border-radius:6px;
}
.tabs button.active { background:#3A6DF0; }
section { display:none; margin-top:20px; }
section.active { display:block; }
input,textarea {
  background:#333; color:white; border:none;
  padding:8px; width:300px; border-radius:4px;
}
</style>
</head>
<body>
<h2>🌐 Удалённое управление InfoKiosk</h2>
<div class='tabs'>
  <button onclick='showTab(0)' class='active'>⚙ Настройки</button>
  <button onclick='showTab(1)'>📁 Файлы</button>
  <button onclick='showTab(2)'>🔄 Система</button>
</div>

<section id='tab0' class='active'>
  <h3>⚙ Текущие настройки</h3>
  <textarea id='configArea' rows='18'></textarea><br>
  <button onclick='saveConfig()'>💾 Сохранить</button>
</section>

<section id='tab1'>
  <h3>📁 Файлы</h3>
  <input type='file' id='fileInput'>
  <button onclick='uploadFile()'>⬆ Загрузить</button>
  <ul id='fileList'></ul>
</section>

<section id='tab2'>
  <h3>🔄 Управление</h3>
  <button onclick='restartApp()'>Перезапустить приложение</button>
</section>

<script>
function showTab(i){
  document.querySelectorAll('.tabs button').forEach((b,j)=>b.classList.toggle('active',i===j));
  document.querySelectorAll('section').forEach((s,j)=>s.classList.toggle('active',i===j));
  if(i===1) loadFiles();
  if(i===0) loadConfig();
}

async function loadConfig(){
  const res = await fetch('/config');
  const txt = await res.text();
  document.getElementById('configArea').value = txt;
}
async function saveConfig(){
  const data = document.getElementById('configArea').value;
  await fetch('/config',{method:'POST',body:data});
  alert('✅ Настройки сохранены');
}

async function loadFiles(){
  const res = await fetch('/files');
  const arr = await res.json();
  const list = document.getElementById('fileList');
  list.innerHTML = '';
  arr.forEach(f=>{
    const li = document.createElement('li');
    li.textContent = f.split('/').pop();
    li.onclick = ()=>deleteFile(li.textContent);
    list.appendChild(li);
  });
}
async function deleteFile(name){
  await fetch('/files?name='+encodeURIComponent(name),{method:'DELETE'});
  loadFiles();
}
async function uploadFile(){
  const file = document.getElementById('fileInput').files[0];
  if(!file) return alert('Выберите файл!');
  await fetch('/upload',{method:'POST',headers:{'X-Filename':file.name},body:file});
  loadFiles();
}
async function restartApp(){
  await fetch('/restart');
  alert('Приложение перезапускается...');
}
loadConfig();
</script>
</body>
</html>";
        }
        #endregion
    }
}