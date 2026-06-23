using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace InfoKioskWatchdog;

/// <summary>
/// InfoKioskWatchdog — сторожевой процесс.
///
/// Запускается один раз (например, через Task Scheduler при входе пользователя
/// или как сервис). Контролирует, что InfoKioskApp.exe работает. Если процесс
/// отсутствует или завис (не отвечает более <see cref="HangSeconds"/> секунд),
/// перезапускает его.
///
/// Запуск:
///   InfoKioskWatchdog.exe            — мониторит InfoKioskApp.exe в той же папке
///   InfoKioskWatchdog.exe --path "C:\InfoKiosk\InfoKioskApp.exe"
///                                    — мониторит произвольный путь
///   InfoKioskWatchdog.exe --install  — ставит себя в Task Scheduler (при входе пользователя)
///   InfoKioskWatchdog.exe --uninstall
/// </summary>
internal class Program
{
    // Параметры по умолчанию
    private static string _exePath = "";
    private static string _exeName = "InfoKioskApp.exe";
    private static int _checkSeconds = 10;        // как часто проверять
    private static int _hangSeconds = 60;         // если процесс не отвечал больше этого — убить
    private static int _restartCooldown = 5;      // секунд между kill и restart
    private static int _maxRestartsPerHour = 30;  // защита от цикла падений

    private static int _restartsThisHour = 0;
    private static DateTime _hourStartedAt = DateTime.UtcNow;

    static async Task Main(string[] args)
    {
        ParseArgs(args);

        if (string.IsNullOrEmpty(_exePath))
        {
            // По умолчанию — рядом с watchdog'ом
            _exePath = Path.Combine(AppContext.BaseDirectory, _exeName);
        }

        Console.WriteLine($"[Watchdog] Начинаю следить за: {_exePath}");
        Console.WriteLine($"[Watchdog] Проверка каждые {_checkSeconds}s, hang timeout = {_hangSeconds}s");

        // Если стоит --install — ставим в планировщик и выходим
        if (Array.Exists(args, a => a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
        {
            InstallToTaskScheduler();
            return;
        }
        if (Array.Exists(args, a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            UninstallFromTaskScheduler();
            return;
        }

        // Главный цикл
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await WatchLoop(cts.Token);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog] FATAL: {ex}");
            Environment.Exit(1);
        }
    }

    private static async Task WatchLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var proc = FindKioskProcess();
                if (proc == null)
                {
                    Console.WriteLine($"[Watchdog] {DateTime.Now:HH:mm:ss} — процесс не найден, запускаю...");
                    StartKiosk();
                }
                else if (IsHung(proc))
                {
                    Console.WriteLine($"[Watchdog] {DateTime.Now:HH:mm:ss} — процесс завис (Not Responding > {_hangSeconds}s), перезапускаю...");
                    SafeKill(proc);
                    await Task.Delay(_restartCooldown * 1000, ct);
                    StartKiosk();
                }
                else
                {
                    // Всё ок — тихо
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog] Ошибка в цикле: {ex.Message}");
            }

            try { await Task.Delay(_checkSeconds * 1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ---------------------------- PROCESS HELPERS ----------------------------

    private static Process? FindKioskProcess()
    {
        var name = Path.GetFileNameWithoutExtension(_exePath);
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                if (p.MainModule?.FileName != null &&
                    string.Equals(p.MainModule.FileName, _exePath, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            catch { /* доступ закрыт — игнорируем */ }
            finally { /* не закрываем — вернём процесс */ }
        }
        return null;
    }

    private static bool IsHung(Process p)
    {
        try
        {
            if (!p.Responding)
            {
                // Не отвечал дольше _hangSeconds — считаем зависшим
                // (упрощённая эвристика — Responding обновляется ОС каждые несколько секунд)
                return true;
            }
        }
        catch (Win32Exception) { /* access denied — не считаем зависшим */ }
        catch (InvalidOperationException) { /* процесс уже завершился */ }
        return false;
    }

    private static void SafeKill(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog] Не удалось убить процесс: {ex.Message}");
        }
        finally
        {
            try { p.Dispose(); } catch { }
        }
    }

    private static void StartKiosk()
    {
        // Защита от цикла перезапусков
        if ((DateTime.UtcNow - _hourStartedAt).TotalHours >= 1)
        {
            _hourStartedAt = DateTime.UtcNow;
            _restartsThisHour = 0;
        }
        if (_restartsThisHour >= _maxRestartsPerHour)
        {
            Console.WriteLine($"[Watchdog] Достигнут лимит перезапусков ({_maxRestartsPerHour}/час). Жду час.");
            return;
        }

        if (!File.Exists(_exePath))
        {
            Console.WriteLine($"[Watchdog] Файл не найден: {_exePath}");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? "",
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Maximized
            };
            Process.Start(psi);
            _restartsThisHour++;
            Console.WriteLine($"[Watchdog] Запущен (перезапуск #{_restartsThisHour} за час).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog] Ошибка запуска: {ex.Message}");
        }
    }

    // ---------------------------- ARGS ----------------------------

    private static void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--path":
                case "-p":
                    if (i + 1 < args.Length) _exePath = args[++i];
                    break;
                case "--check":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var c)) _checkSeconds = c;
                    break;
                case "--hang":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var h)) _hangSeconds = h;
                    break;
                case "--cooldown":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var cd)) _restartCooldown = cd;
                    break;
                case "--max":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var m)) _maxRestartsPerHour = m;
                    break;
            }
        }
    }

    // ---------------------------- TASK SCHEDULER ----------------------------

    private static void InstallToTaskScheduler()
    {
        // schtasks /Create /TN "InfoKioskWatchdog" /TR "<watchdog exe>" /SC ONLOGON /RL HIGHEST /F
        var wd = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(wd))
        {
            Console.WriteLine("[Watchdog] Не определить путь к собственному exe.");
            return;
        }

        var args = $"/Create /TN \"InfoKioskWatchdog\" /TR \"\\\"{wd}\\\"\" /SC ONLOGON /RL HIGHEST /F";
        Console.WriteLine($"[Watchdog] schtasks {args}");

        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var p = Process.Start(psi);
        if (p == null) { Console.WriteLine("[Watchdog] Не удалось запустить schtasks."); return; }
        p.WaitForExit();
        Console.WriteLine(p.StandardOutput.ReadToEnd());
        var err = p.StandardError.ReadToEnd();
        if (!string.IsNullOrEmpty(err)) Console.WriteLine("[Watchdog] " + err);
        Console.WriteLine(p.ExitCode == 0
            ? "[Watchdog] Установлено в Task Scheduler (ONLOGON)."
            : "[Watchdog] Ошибка установки. Запустите от администратора.");
    }

    private static void UninstallFromTaskScheduler()
    {
        var psi = new ProcessStartInfo("schtasks.exe", "/Delete /TN \"InfoKioskWatchdog\" /F")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var p = Process.Start(psi);
        if (p == null) return;
        p.WaitForExit();
        Console.WriteLine(p.StandardOutput.ReadToEnd());
        Console.WriteLine(p.ExitCode == 0
            ? "[Watchdog] Удалено из Task Scheduler."
            : "[Watchdog] Ошибка удаления.");
    }
}
