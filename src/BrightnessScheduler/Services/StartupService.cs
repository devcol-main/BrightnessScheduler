using System;
using Microsoft.Win32;

namespace BrightnessScheduler.Services;

/// <summary>Per-user "Run at Windows startup" via HKCU\...\Run (no admin rights needed).</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BrightnessScheduler";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to update startup registration", ex);
        }
    }
}
