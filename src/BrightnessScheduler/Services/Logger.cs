using System;
using System.IO;
using System.Linq;

namespace BrightnessScheduler.Services;

public static class Logger
{
    private static readonly object Gate = new();
    public static string LogPath => Path.Combine(SettingsService.DataFolder, "app.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsService.DataFolder);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
                var fi = new FileInfo(LogPath);
                if (fi.Length > 1024 * 1024)
                {
                    var tail = File.ReadAllLines(LogPath).TakeLast(2000).ToArray();
                    File.WriteAllLines(LogPath, tail);
                }
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
