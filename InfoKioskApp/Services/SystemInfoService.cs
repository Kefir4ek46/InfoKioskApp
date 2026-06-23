using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace InfoKioskApp.Services
{
    public static class SystemInfoService
    {
        // IMP: для GlobalMemoryStatusEx — позволяет получить общий объём RAM на машине.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static DriveInfo GetSystemDrive()
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory);
            return new DriveInfo(root);
        }

        public static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path))
                return 0;

            long size = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch { }
                }
            }
            catch { }

            return size;
        }

        public static string FormatBytes(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Возвращает общий объём физической памяти (RAM) машины в байтах.
        /// Использует Win32 GlobalMemoryStatusEx — на Windows это самый надёжный способ
        /// получить total RAM из .NET без перформанс-счётчиков.
        /// </summary>
        public static long GetTotalPhysicalMemoryBytes()
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                    return (long)mem.ullTotalPhys;
            }
            catch { }
            return 0;
        }

        public static long GetAvailablePhysicalMemoryBytes()
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                    return (long)mem.ullAvailPhys;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Агрегированный снимок состояния системы для /api/info и админки.
        /// Возвращает диск, память, CPU, процесс, время работы — всё в одном объекте.
        /// Логика та же, что в HandleStorageInfo + GetPerformanceSnapshot, но
        /// переиспользуется через SystemInfoService.GetInfo(), чтобы не дублировать
        /// код в нескольких местах.
        /// </summary>
        public static object GetInfo()
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                var drive = GetSystemDrive();
                long totalRam = GetTotalPhysicalMemoryBytes();
                long freeRam = GetAvailablePhysicalMemoryBytes();

                var gcInfo = GC.GetGCMemoryInfo();
                long totalAvailableManaged = gcInfo.TotalAvailableMemoryBytes > 0
                    ? gcInfo.TotalAvailableMemoryBytes
                    : 0;
                long usedManaged = GC.GetTotalMemory(false);

                return new
                {
                    machine = new
                    {
                        machineName = Environment.MachineName,
                        osVersion = Environment.OSVersion?.VersionString ?? "—",
                        processorCount = Environment.ProcessorCount,
                        is64Bit = Environment.Is64BitOperatingSystem,
                        systemDirectory = Environment.SystemDirectory,
                        uptime = (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss")
                    },
                    memory = new
                    {
                        totalBytes = totalRam,
                        total = totalRam > 0 ? FormatBytes(totalRam) : "—",
                        availableBytes = freeRam,
                        available = freeRam > 0 ? FormatBytes(freeRam) : "—",
                        usedBytes = totalRam > freeRam ? (totalRam - freeRam) : 0,
                        used = totalRam > freeRam ? FormatBytes(totalRam - freeRam) : "—",
                        managedBytes = usedManaged,
                        managed = FormatBytes(usedManaged),
                        managedAvailableBytes = totalAvailableManaged,
                        managedAvailable = totalAvailableManaged > 0 ? FormatBytes(totalAvailableManaged) : "—"
                    },
                    disk = new
                    {
                        name = drive.Name,
                        totalBytes = drive.TotalSize,
                        usedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                        freeBytes = drive.AvailableFreeSpace,
                        total = FormatBytes(drive.TotalSize),
                        used = FormatBytes(drive.TotalSize - drive.AvailableFreeSpace),
                        free = FormatBytes(drive.AvailableFreeSpace)
                    },
                    process = new
                    {
                        name = proc.ProcessName,
                        pid = proc.Id,
                        baseDir = AppDomain.CurrentDomain.BaseDirectory,
                        workingSetBytes = proc.WorkingSet64,
                        workingSet = FormatBytes(proc.WorkingSet64),
                        startTime = proc.StartTime,
                        uptime = (DateTime.Now - proc.StartTime).ToString(@"dd\.hh\:mm\:ss"),
                        threads = proc.Threads.Count
                    },
                    timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }
    }
}
