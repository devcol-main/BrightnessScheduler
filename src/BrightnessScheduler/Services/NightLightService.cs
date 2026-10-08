// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace BrightnessScheduler.Services;

/// <summary>Last known Windows Night Light state.</summary>
public sealed record NightLightState(bool Known, bool Enabled, int Strength, bool WindowsScheduleOn);

/// <summary>
/// Controls Windows Night Light (Settings › Display › Night light).
///
/// Windows has no public API for Night Light, and on recent builds (24H2/25H2) editing the
/// CloudStore registry blobs is no longer picked up. The only reliable way is the Settings app
/// itself, so this service opens "ms-settings:nightlight" for about a second, presses
/// "Turn on now / Turn off now", sets the strength slider (committed with a key press, which is
/// what makes Windows apply it), then closes the window again.
///
/// UI Automation ids used (language independent):
///   SystemSettings_Display_BlueLight_ManualToggleOn_Button / ..._ManualToggleOff_Button
///   SystemSettings_Display_BlueLight_ColorTemperature_Slider
///   SystemSettings_Display_BlueLight_AutomaticOnSchedule_ToggleSwitch
/// </summary>
public static class NightLightService
{
    private const string IdOn = "SystemSettings_Display_BlueLight_ManualToggleOn_Button";
    private const string IdOff = "SystemSettings_Display_BlueLight_ManualToggleOff_Button";
    private const string IdSlider = "SystemSettings_Display_BlueLight_ColorTemperature_Slider";
    private const string IdSchedule = "SystemSettings_Display_BlueLight_AutomaticOnSchedule_ToggleSwitch";

    private static readonly object Gate = new();
    private static NightLightState _last = new(false, false, 50, ReadWindowsScheduleFlag());

    public static NightLightState Get() => _last with { WindowsScheduleOn = ReadWindowsScheduleFlag() };

    /// <summary>Seeds the cache with the last saved state (still marked as not checked).</summary>
    public static void Seed(bool enabled, int strength) => _last = _last with { Enabled = enabled, Strength = Math.Clamp(strength, 0, 100) };

    /// <summary>Reads the real state by briefly opening Settings.</summary>
    public static NightLightState Refresh() => Apply(null, null, false);

    /// <summary>Turns Night Light on/off and/or sets its strength (0–100). Blocking (~1 s).</summary>
    public static NightLightState Set(bool? enabled, int? strength) => Apply(enabled, strength, false);

    /// <summary>Turns off Windows' own Night Light schedule so it doesn't fight ours.</summary>
    public static NightLightState DisableWindowsSchedule() => Apply(null, null, true);

    private static NightLightState Apply(bool? enabled, int? strength, bool disableSchedule)
    {
        lock (Gate)
        {
            bool wasRunning = Process.GetProcessesByName("SystemSettings").Length > 0;
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:nightlight") { UseShellExecute = true });
                var (window, slider) = WaitForPage(TimeSpan.FromSeconds(8));
                if (window == null || slider == null)
                {
                    Logger.Warn("Night Light: Settings page not found");
                    return _last;
                }

                if (enabled.HasValue)
                {
                    var btn = Find(window, enabled.Value ? IdOn : IdOff);
                    if (btn?.TryGetCurrentPattern(InvokePattern.Pattern, out var p) == true)
                    {
                        ((InvokePattern)p).Invoke();
                        Thread.Sleep(250);
                    }
                }

                if (strength.HasValue)
                    SetSlider(window, slider, Math.Clamp(strength.Value, 0, 100));

                if (disableSchedule)
                {
                    var sw = Find(window, IdSchedule);
                    if (sw?.TryGetCurrentPattern(TogglePattern.Pattern, out var tp) == true
                        && ((TogglePattern)tp).Current.ToggleState == ToggleState.On)
                    {
                        ((TogglePattern)tp).Toggle();
                        Thread.Sleep(250);
                    }
                }

                // Read back the state Windows actually has.
                bool isOn = Find(window, IdOff) != null;
                int value = slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var rv)
                    ? (int)Math.Round(((RangeValuePattern)rv).Current.Value) : _last.Strength;
                _last = new NightLightState(true, isOn, value, ReadWindowsScheduleFlag());
                Logger.Info($"Night Light: on={isOn} strength={value}");

                if (!wasRunning) Close(window);
            }
            catch (Exception ex)
            {
                Logger.Warn("Night Light control failed: " + ex.Message);
            }
            return _last;
        }
    }

    private static (AutomationElement? Window, AutomationElement? Slider) WaitForPage(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        var frameCond = new PropertyCondition(AutomationElement.ClassNameProperty, "ApplicationFrameWindow");
        while (sw.Elapsed < timeout)
        {
            Thread.Sleep(150);
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, frameCond))
            {
                var slider = Find(w, IdSlider);
                if (slider != null) return (w, slider);
            }
        }
        return (null, null);
    }

    private static AutomationElement? Find(AutomationElement root, string automationId)
        => root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    /// <summary>
    /// Setting the value through UI Automation only moves the thumb; Windows applies the strength
    /// on a keyboard change. So: set to one step away, then press the arrow key once.
    /// </summary>
    private static void SetSlider(AutomationElement window, AutomationElement slider, int target)
    {
        if (!slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var p)) return;
        var rv = (RangeValuePattern)p;
        int current = (int)Math.Round(rv.Current.Value);
        if (current == target) return;

        bool up = target > 0;
        rv.SetValue(up ? target - 1 : target + 1);
        slider.SetFocus();
        Thread.Sleep(120);

        var hwnd = new IntPtr(window.Current.NativeWindowHandle);
        if (GetForegroundWindow() != hwnd) SetForegroundWindow(hwnd);
        if (GetForegroundWindow() != hwnd)
        {
            Logger.Warn("Night Light: Settings not in foreground, strength not committed");
            return;
        }
        PressKey(up ? VK_RIGHT : VK_LEFT);
        Thread.Sleep(200);
    }

    private static void Close(AutomationElement window)
    {
        try
        {
            if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var wp)) ((WindowPattern)wp).Close();
        }
        catch
        {
            foreach (var pr in Process.GetProcessesByName("SystemSettings")) { try { pr.Kill(); } catch { } }
        }
    }

    /// <summary>Best effort: Windows' own Night Light schedule flag from the CloudStore settings blob.</summary>
    private static bool ReadWindowsScheduleFlag()
    {
        try
        {
            const string baseKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current";
            using var k = Registry.CurrentUser.OpenSubKey(baseKey);
            var sub = k?.GetSubKeyNames().FirstOrDefault(n => n.StartsWith("default$windows.data.bluelightreduction.settings", StringComparison.OrdinalIgnoreCase));
            if (sub == null) return false;
            using var v = k!.OpenSubKey($@"{sub}\{sub[(sub.IndexOf('$') + 1)..]}");
            if (v?.GetValue("Data") is not byte[] d) return false;
            // payload starts after "2A 2B 0E <len>": 43 42 01 00 [02 01 = schedule on]
            int i = IndexOf(d, new byte[] { 0x2A, 0x2B, 0x0E }, 4);
            if (i < 0) return false;
            int p = i + 3;
            while (p < d.Length && (d[p] & 0x80) != 0) p++;
            p++; // past length varint
            return p + 5 < d.Length && d[p + 4] == 0x02 && d[p + 5] == 0x01;
        }
        catch { return false; }
    }

    private static int IndexOf(byte[] d, byte[] pattern, int from)
    {
        for (int i = from; i <= d.Length - pattern.Length; i++)
        {
            bool m = true;
            for (int j = 0; j < pattern.Length && m; j++) m = d[i + j] == pattern[j];
            if (m) return i;
        }
        return -1;
    }

    // ---------------- input ----------------
    private const ushort VK_LEFT = 0x25, VK_RIGHT = 0x27;

    private static void PressKey(ushort vk)
    {
        var inputs = new[]
        {
            new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = 0x0001 } } },
            new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = 0x0001 | 0x0002 } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
}
