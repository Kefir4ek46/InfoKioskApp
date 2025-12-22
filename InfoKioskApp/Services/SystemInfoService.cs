using System;
using System.IO;

namespace InfoKioskApp.Services
{
    public static class SystemInfoService
    {
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
    }
}
