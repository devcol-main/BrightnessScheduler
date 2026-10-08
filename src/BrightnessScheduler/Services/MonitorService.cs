// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using BrightnessScheduler.Native;
using static BrightnessScheduler.Native.NativeMethods;

namespace BrightnessScheduler.Services;

public enum DisplayProperty { Brightness, Contrast }

/// <summary>Snapshot of a connected display and what it supports.</summary>
public sealed class DisplayInfo
{
    public string Id { get; init; } = "";          // e.g. "SAM093A#5&936221D&0&UID8449"
    public string Model { get; init; } = "";       // e.g. "SAM093A"
    public string FriendlyName { get; init; } = "";
    public int DisplayNumber { get; init; }
    public bool IsInternal { get; init; }
    public bool UsesWmi { get; init; }
    public bool SupportsBrightness { get; init; }
    public bool SupportsContrast { get; init; }
    public int Brightness { get; set; } = -1;     // percent, -1 = unknown
    public int Contrast { get; set; } = -1;
}

/// <summary>
/// Talks to displays directly:
///  - built-in laptop panels via WMI (WmiMonitorBrightnessMethods)
///  - external monitors via DDC/CI (dxva2.dll, VCP 0x10 brightness / 0x12 contrast)
/// All hardware access is serialized through <see cref="HardwareLock"/>.
/// </summary>
public static class MonitorService
{
    public static readonly object HardwareLock = new();

    public static List<DisplayInfo> Detect()
    {
        lock (HardwareLock)
        {
            var result = new List<DisplayInfo>();
            var wmi = WmiBrightness.GetAll();
            using var session = DisplaySession.OpenUnlocked();
            foreach (var m in session.Monitors)
            {
                bool usesWmi = wmi.TryGetValue(m.Id, out var wmiValue);
                int ddcB = usesWmi ? -1 : session.GetUnlocked(m, DisplayProperty.Brightness, attempts: 3);
                int ddcC = session.GetUnlocked(m, DisplayProperty.Contrast, attempts: usesWmi ? 1 : 3);
                result.Add(new DisplayInfo
                {
                    Id = m.Id,
                    Model = m.Model,
                    FriendlyName = m.FriendlyName,
                    DisplayNumber = m.DisplayNumber,
                    IsInternal = m.IsInternal || usesWmi,
                    UsesWmi = usesWmi,
                    SupportsBrightness = usesWmi || ddcB >= 0,
                    SupportsContrast = ddcC >= 0,
                    Brightness = usesWmi ? wmiValue : ddcB,
                    Contrast = ddcC,
                });
            }
            // WMI-only panels that had no physical-monitor handle (rare)
            foreach (var kv in wmi.Where(kv => result.All(r => r.Id != kv.Key)))
            {
                result.Add(new DisplayInfo
                {
                    Id = kv.Key, Model = ModelOf(kv.Key), IsInternal = true, UsesWmi = true,
                    SupportsBrightness = true, Brightness = kv.Value,
                });
            }
            Logger.Info("Detected displays: " + string.Join(", ", result.Select(r =>
                $"{r.Id} [{(r.UsesWmi ? "WMI" : "DDC")}] B={r.SupportsBrightness}:{r.Brightness} C={r.SupportsContrast}:{r.Contrast}")));
            return result;
        }
    }

    /// <summary>Sets a single value right away (manual slider control).</summary>
    public static bool SetValue(string id, DisplayProperty prop, int percent)
    {
        lock (HardwareLock)
        {
            using var s = DisplaySession.OpenUnlocked();
            return s.SetUnlocked(id, prop, percent);
        }
    }

    internal static string NormalizeDevicePath(string devicePath)
    {
        // "\\?\DISPLAY#SAM093A#5&936221d&0&UID8449#{e6f07b5f-...}" -> "SAM093A#5&936221D&0&UID8449"
        var parts = devicePath.Split('#');
        return parts.Length >= 3 ? (parts[1] + "#" + parts[2]).ToUpperInvariant() : devicePath.ToUpperInvariant();
    }

    internal static string NormalizeWmiInstance(string instanceName)
    {
        // "DISPLAY\SDC420A\4&173bac7d&0&UID8388688_0" -> "SDC420A#4&173BAC7D&0&UID8388688"
        var parts = instanceName.Split('\\');
        if (parts.Length < 3) return instanceName.ToUpperInvariant();
        var inst = Regex.Replace(parts[2], @"_\d+$", "");
        return (parts[1] + "#" + inst).ToUpperInvariant();
    }

    public static string ModelOf(string id)
    {
        int i = id.IndexOf('#');
        return i > 0 ? id[..i] : id;
    }
}

/// <summary>Built-in panel brightness via WMI.</summary>
internal static class WmiBrightness
{
    public static Dictionary<string, int> GetAll()
    {
        var map = new Dictionary<string, int>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentBrightness, Active FROM WmiMonitorBrightness");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    if (o["Active"] is bool active && !active) continue;
                    var id = MonitorService.NormalizeWmiInstance((string)o["InstanceName"]);
                    map[id] = Convert.ToInt32(o["CurrentBrightness"]);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // No WMI-controllable panel (desktop PC etc.)
        }
        return map;
    }

    public static bool Set(string id, int percent)
    {
        try
        {
            using var cls = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
            bool any = false;
            foreach (ManagementObject o in cls.GetInstances())
            {
                using (o)
                {
                    if (MonitorService.NormalizeWmiInstance((string)o["InstanceName"]) != id) continue;
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Clamp(percent, 0, 100) });
                    any = true;
                }
            }
            return any;
        }
        catch (Exception ex)
        {
            Logger.Warn($"WMI set brightness failed for {id}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// An open set of physical-monitor handles. Callers must hold <see cref="MonitorService.HardwareLock"/>.
/// </summary>
internal sealed class DisplaySession : IDisposable
{
    public sealed class Monitor
    {
        public string Id = "";
        public string Model = "";
        public string FriendlyName = "";
        public int DisplayNumber;
        public bool IsInternal;
        public IntPtr Handle;
        public uint MaxBrightness, MaxContrast;
    }

    private readonly List<PHYSICAL_MONITOR[]> _arrays = new();
    private readonly List<Monitor> _monitors = new();
    private Dictionary<string, int>? _wmiCache;
    public IReadOnlyList<Monitor> Monitors => _monitors;

    private DisplaySession() { }

    public static DisplaySession OpenUnlocked()
    {
        var s = new DisplaySession();
        var targets = QueryTargets();

        var hMonitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) => { hMonitors.Add(h); return true; }, IntPtr.Zero);

        foreach (var hMon in hMonitors)
        {
            var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(hMon, ref mi);
            var gdi = mi.szDevice ?? "";
            var matching = targets.Where(t => string.Equals(t.Gdi, gdi, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint n) || n == 0) continue;
            var arr = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) continue;
            s._arrays.Add(arr);

            for (int i = 0; i < arr.Length; i++)
            {
                var t = i < matching.Count ? matching[i] : default;
                string id = t.Path is { Length: > 0 }
                    ? MonitorService.NormalizeDevicePath(t.Path)
                    : $"UNKNOWN#{gdi.TrimStart('\\', '.')}_{i}".ToUpperInvariant();
                s._monitors.Add(new Monitor
                {
                    Id = id,
                    Model = MonitorService.ModelOf(id),
                    FriendlyName = string.IsNullOrWhiteSpace(t.Friendly) ? (arr[i].szPhysicalMonitorDescription ?? "") : t.Friendly,
                    DisplayNumber = ParseDisplayNumber(gdi),
                    IsInternal = t.Internal,
                    Handle = arr[i].hPhysicalMonitor,
                });
            }
        }
        return s;
    }

    private static int ParseDisplayNumber(string gdi)
    {
        var m = Regex.Match(gdi, @"(\d+)$");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    private bool IsWmi(string id)
    {
        _wmiCache ??= WmiBrightness.GetAll();
        return _wmiCache.ContainsKey(id);
    }

    /// <summary>Returns the current value in percent, or -1 if unsupported.</summary>
    public int GetUnlocked(Monitor m, DisplayProperty prop, int attempts = 2)
    {
        if (prop == DisplayProperty.Brightness && IsWmi(m.Id))
            return _wmiCache![m.Id];

        byte code = prop == DisplayProperty.Brightness ? VcpBrightness : VcpContrast;
        for (int i = 0; i < attempts; i++)
        {
            if (GetVCPFeatureAndVCPFeatureReply(m.Handle, code, IntPtr.Zero, out uint cur, out uint max) && max > 0)
            {
                if (prop == DisplayProperty.Brightness) m.MaxBrightness = max; else m.MaxContrast = max;
                return (int)Math.Round(cur * 100.0 / max);
            }
            Thread.Sleep(60);
        }
        return -1;
    }

    public int GetUnlocked(string id, DisplayProperty prop)
    {
        var m = _monitors.FirstOrDefault(x => x.Id == id);
        if (m == null)
            return prop == DisplayProperty.Brightness && IsWmi(id) ? _wmiCache![id] : -1;
        return GetUnlocked(m, prop);
    }

    public bool SetUnlocked(string id, DisplayProperty prop, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (prop == DisplayProperty.Brightness && IsWmi(id))
        {
            bool ok = WmiBrightness.Set(id, percent);
            if (ok) _wmiCache![id] = percent;
            return ok;
        }

        var m = _monitors.FirstOrDefault(x => x.Id == id);
        if (m == null) return false;

        uint max = prop == DisplayProperty.Brightness ? m.MaxBrightness : m.MaxContrast;
        if (max == 0)
        {
            if (GetUnlocked(m, prop) < 0) return false;
            max = prop == DisplayProperty.Brightness ? m.MaxBrightness : m.MaxContrast;
        }
        uint value = (uint)Math.Round(max * percent / 100.0);
        byte code = prop == DisplayProperty.Brightness ? VcpBrightness : VcpContrast;
        for (int i = 0; i < 3; i++)
        {
            if (SetVCPFeature(m.Handle, code, value)) return true;
            Thread.Sleep(100);
        }
        Logger.Warn($"DDC/CI set {prop}={percent} failed for {id}");
        return false;
    }

    public void Dispose()
    {
        foreach (var arr in _arrays)
        {
            try { DestroyPhysicalMonitors((uint)arr.Length, arr); } catch { /* ignore */ }
        }
        _arrays.Clear();
    }

    private record struct Target(string Gdi, string Path, string Friendly, bool Internal);

    private static List<Target> QueryTargets()
    {
        var list = new List<Target>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return list;
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;

            for (int i = 0; i < np; i++)
            {
                var p = paths[i];
                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                src.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                src.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                src.header.adapterId = p.sourceInfo.adapterId;
                src.header.id = p.sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;

                var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                tgt.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                tgt.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                tgt.header.adapterId = p.targetInfo.adapterId;
                tgt.header.id = p.targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;

                list.Add(new Target(src.viewGdiDeviceName, tgt.monitorDevicePath, tgt.monitorFriendlyDeviceName,
                    IsInternalTechnology(tgt.outputTechnology)));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("QueryDisplayConfig failed: " + ex.Message);
        }
        return list;
    }
}
