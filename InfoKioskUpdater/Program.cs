using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

class Program
{
    private const string MutexName = "Global\\InfoKioskUpdaterMutex";
    private const string LockFile = "update.lock";
    private const string LogFile = "updater.log";
    private const string ConfigFile = "updater.config.json";

    static int Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            Respond(false, "busy", "Updater already running");
            return 1;
        }

        try
        {
            if (args.Length == 0)
                return Help();

            return args[0].ToLower() switch
            {
                "check" => Check(),
                "update" => Update(),
                "rollback" => Rollback(),
                _ => Help()
            };
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
            Respond(false, "fatal", ex.Message);
            return 1;
        }
    }

    // ================= CHECK =================
    static int Check()
    {
        Log("CHECK started");

        var cfg = LoadConfig();
        var current = ReadVersion(cfg.InstallDir);
        var latest = GetLatestRelease(cfg);

        Respond(true, "check", "ok", new
        {
            currentVersion = current,
            latestVersion = latest.Version,
            updateAvailable = current != latest.Version
        });

        return 0;
    }

    // ================= UPDATE =================
    static int Update()
    {
        if (File.Exists(LockFile))
            throw new Exception("Update already running");

        File.WriteAllText(LockFile, DateTime.Now.ToString());

        try
        {
            var cfg = LoadConfig();
            var release = GetLatestRelease(cfg);

            KillApp(cfg);

            var tempDir = Path.Combine(Path.GetTempPath(), "InfoKioskUpdate");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);

            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "update.zip");
            Download(release.ZipUrl, zipPath);

            ZipFile.ExtractToDirectory(zipPath, tempDir, true);

            CreateBackup(cfg.InstallDir);
            ApplyUpdate(tempDir, cfg.InstallDir);

            File.WriteAllText(
                Path.Combine(cfg.InstallDir, "version.txt"),
                release.Version
            );

            StartApp(cfg);

            Respond(true, "update", "updated", new
            {
                version = release.Version
            });

            return 0;
        }
        catch (Exception ex)
        {
            Log("UPDATE FAILED: " + ex);
            Respond(false, "update", ex.Message);
            return 1;
        }
        finally
        {
            File.Delete(LockFile);
        }
    }

    // ================= ROLLBACK =================
    static int Rollback()
    {
        var cfg = LoadConfig();
        var backup = GetLatestBackup(cfg.InstallDir);
        if (backup == null)
            throw new Exception("No backup found");

        KillApp(cfg);
        ApplyUpdate(backup, cfg.InstallDir);

        var versionFile = Path.Combine(backup, "version.txt");
        if (File.Exists(versionFile))
        {
            File.Copy(
                versionFile,
                Path.Combine(cfg.InstallDir, "version.txt"),
                true
            );
        }

        StartApp(cfg);

        Respond(true, "rollback", "rollback complete");
        return 0;
    }

    // ================= HELPERS =================

    static void ApplyUpdate(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var fileName = Path.GetFileName(file);

            if (rel.StartsWith("data") ||
                rel.StartsWith("config") ||
                rel.StartsWith("backups"))
                continue;

            if (fileName.Equals("updater.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("updater.dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    static void CreateBackup(string dir)
    {
        var root = Path.Combine(dir, "backups");
        Directory.CreateDirectory(root);

        var backup = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(backup);

        ApplyUpdate(dir, backup);
    }

    static string? GetLatestBackup(string dir)
    {
        var root = Path.Combine(dir, "backups");
        if (!Directory.Exists(root)) return null;

        var dirs = Directory.GetDirectories(root);
        Array.Sort(dirs);
        return dirs.Length == 0 ? null : dirs[^1];
    }

    static void KillApp(Config cfg)
    {
        var name = Path.GetFileNameWithoutExtension(cfg.AppExeName);
        var currentPid = Process.GetCurrentProcess().Id;

        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                if (p.Id == currentPid)
                    continue;

                var path = p.MainModule!.FileName;
                if (!path.StartsWith(cfg.InstallDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                p.Kill();
                p.WaitForExit();
            }
            catch { }
        }
    }

    static void StartApp(Config cfg)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(cfg.InstallDir, cfg.AppExeName),
            WorkingDirectory = cfg.InstallDir
        });
    }

    static string ReadVersion(string installDir)
    {
        var path = Path.Combine(installDir, "version.txt");
        return File.Exists(path)
            ? File.ReadAllText(path).Trim()
            : "0.0.0";
    }

    static void Download(string url, string path)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("InfoKioskUpdater");
        var data = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        File.WriteAllBytes(path, data);
    }

    static (string Version, string ZipUrl) GetLatestRelease(Config cfg)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("InfoKioskUpdater");

        var json = http.GetStringAsync(cfg.RepoApiUrl).GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind == JsonValueKind.Object)
            return ParseRelease(doc.RootElement);

        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.GetProperty("prerelease").GetBoolean() && !cfg.AllowPrerelease)
                continue;

            return ParseRelease(r);
        }

        throw new Exception("No suitable release");
    }

    static (string Version, string ZipUrl) ParseRelease(JsonElement release)
    {
        var tag = release.GetProperty("tag_name").GetString()!;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString()!;
            if (name.EndsWith(".zip") && !name.StartsWith("Source"))
            {
                return (
                    tag,
                    asset.GetProperty("browser_download_url").GetString()!
                );
            }
        }

        throw new Exception("ZIP asset not found");
    }

    static Config LoadConfig()
    {
        return JsonSerializer.Deserialize<Config>(
            File.ReadAllText(ConfigFile)
        )!;
    }

    static void Log(string msg)
    {
        File.AppendAllText(
            LogFile,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n"
        );
    }

    // ===== REMOTE FRIENDLY JSON =====
    static void Respond(bool ok, string action, string message, object? data = null)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok,
            action,
            message,
            data
        }));
    }

    static int Help()
    {
        Respond(false, "help", "Usage: updater check | update | rollback");
        return 1;
    }
}

class Config
{
    public string RepoApiUrl { get; set; } = "";
    public string AppExeName { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public bool AllowPrerelease { get; set; }
}
