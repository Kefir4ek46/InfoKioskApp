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
            Console.WriteLine("Updater is already running");
            Log("Updater is already running");
            return ExitCodes.Error;
        }

        try
        {
            if (args.Length == 0)

            return args[0].ToLower() switch
            {
            };
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
        }
    }

    {


        {
            latestVersion = latest.Version,
        });

    }

    {
        if (File.Exists(LockFile))

        File.WriteAllText(LockFile, DateTime.Now.ToString());

        try
        {






        }
        catch (Exception ex)
        {
        }
        finally
        {
            if (File.Exists(LockFile))
                File.Delete(LockFile);
        }
    }

    {


        {




            {
                    continue;

            }
    }

            {
            }


        }
        catch (Exception ex)
        {
        }
        finally
        {
            if (File.Exists(LockFile))
                File.Delete(LockFile);
        }
    }

    // =========================
    // HELPERS
    // =========================
    static void KillApp(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        foreach (var p in Process.GetProcessesByName(name))
            p.Kill();
    }

    {
        Process.Start(new ProcessStartInfo
        {
        });
    }

    {

        {

    }

    {



    {


    }

    {

        {
        }

        throw new Exception("ZIP asset not found");
    }

    static Config LoadConfig()
    {
    }

    static void Log(string msg)
    {
    }

    {
        {
            ok,
            action,
            message,
            data
    }

    {
    }
}

// =========================
// MODELS
// =========================
class Config
{
    public string InstallDir { get; set; } = "";
}
