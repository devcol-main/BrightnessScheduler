using System;
using System.IO;
using System.Text.Json;
using BrightnessScheduler.Models;

namespace BrightnessScheduler.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Portable mode: if a settings.json sits next to the exe, it is used.
    /// Otherwise %APPDATA%\BrightnessScheduler.
    /// </summary>
    public static string DataFolder { get; } = ResolveDataFolder();

    public static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public static bool IsFirstRun { get; private set; }

    private static string ResolveDataFolder()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "settings.json"))) return exeDir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrightnessScheduler");
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options);
                if (s != null) return s;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load settings, using defaults", ex);
            try { File.Copy(SettingsPath, SettingsPath + ".bak", true); } catch { }
        }
        IsFirstRun = true;
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, SettingsPath, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save settings", ex);
        }
    }
}
