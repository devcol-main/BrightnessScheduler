// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace BrightnessScheduler.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    /// <summary>"auto", "ko" or "en"</summary>
    public string Language { get; set; } = "auto";
    /// <summary>"system", "light" or "dark"</summary>
    public string Theme { get; set; } = "system";
    public bool RunAtStartup { get; set; } = true;
    /// <summary>Fade duration when a schedule entry starts (0 = instant).</summary>
    public int TransitionSeconds { get; set; } = 0;
    /// <summary>Re-apply after resume from sleep, unlock and display changes.</summary>
    public bool ReapplyOnSystemEvents { get; set; } = true;
    /// <summary>Re-apply the active entry every N minutes (0 = off).</summary>
    public int EnforceIntervalMinutes { get; set; } = 0;
    public bool ShowNotifications { get; set; } = true;
    public List<ScheduleEntry> Entries { get; set; } = new();
}

public sealed class ScheduleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Start time, "HH:mm".</summary>
    public string Time { get; set; } = "22:00";
    /// <summary>Days of week (0 = Sunday … 6 = Saturday) this entry starts on.</summary>
    public List<int> Days { get; set; } = new() { 0, 1, 2, 3, 4, 5, 6 };
    public List<MonitorTarget> Targets { get; set; } = new();

    public TimeSpan StartTime
    {
        get => TimeSpan.TryParse(Time, out var t) ? new TimeSpan(t.Hours, t.Minutes, 0) : TimeSpan.Zero;
    }

    /// <summary>Exact id match first, then the same monitor model (handles port / instance changes).</summary>
    public MonitorTarget? FindTarget(string monitorId)
    {
        var exact = Targets.FirstOrDefault(t => string.Equals(t.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        var model = Services.MonitorService.ModelOf(monitorId);
        return Targets.FirstOrDefault(t => string.Equals(Services.MonitorService.ModelOf(t.MonitorId), model, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class MonitorTarget
{
    public string MonitorId { get; set; } = "";
    /// <summary>Last known display name (informational).</summary>
    public string MonitorName { get; set; } = "";
    public bool SetBrightness { get; set; }
    public int Brightness { get; set; } = 100;
    public bool SetContrast { get; set; }
    public int Contrast { get; set; } = 50;
}
