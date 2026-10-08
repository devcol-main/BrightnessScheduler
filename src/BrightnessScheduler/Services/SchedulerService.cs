// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BrightnessScheduler.Models;
using Microsoft.Win32;

namespace BrightnessScheduler.Services;

/// <summary>
/// Decides which schedule entry is active and pushes its values to the displays.
/// All public members must be called on the UI thread; hardware work runs on the thread pool.
/// </summary>
public sealed class SchedulerService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _eventDelay;
    private bool _displaysChangedPending;
    private string? _lastKey;
    private DateTime _lastApply = DateTime.MinValue;
    private CancellationTokenSource? _cts;

    public ScheduleEntry? ActiveEntry { get; private set; }
    public DateTime ActiveSince { get; private set; }
    public ScheduleEntry? NextEntry { get; private set; }
    public DateTime NextAt { get; private set; }
    public DateTime? PausedUntil { get; private set; }
    public bool IsPaused => PausedUntil.HasValue && PausedUntil.Value > DateTime.Now;

    /// <summary>Entry chosen manually from the mode switcher; stays until <see cref="OverrideUntil"/>.</summary>
    public ScheduleEntry? OverrideEntry { get; private set; }
    public DateTime OverrideUntil { get; private set; }
    private DateTime _overrideSince;
    public bool IsOverridden => OverrideEntry != null;

    /// <summary>The entry whose values are actually in effect (override or schedule).</summary>
    public ScheduleEntry? EffectiveEntry => OverrideEntry ?? ActiveEntry;

    /// <summary>Status (active / next / paused) changed.</summary>
    public event EventHandler? StateChanged;
    /// <summary>Values were written. Key = monitor id, value = (brightness, contrast), -1 = untouched.</summary>
    public event Action<Dictionary<string, (int B, int C)>>? Applied;
    /// <summary>A new entry took over on schedule (for notifications).</summary>
    public event Action<ScheduleEntry>? EntryStarted;
    /// <summary>Monitors were connected / disconnected / reconfigured.</summary>
    public event Action? DisplaysChanged;

    public SchedulerService(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Evaluate();
        _eventDelay = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _eventDelay.Tick += OnEventDelayElapsed;
    }

    public void Start()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        _timer.Start();
        Evaluate();
    }

    /// <summary>Re-evaluates the schedule; applies if the active entry changed.</summary>
    /// <param name="user">User-initiated: always re-apply Night Light even if it looks unchanged.</param>
    public void Evaluate(bool force = false, bool user = false)
    {
        var now = DateTime.Now;
        if (PausedUntil.HasValue && PausedUntil.Value <= now)
        {
            PausedUntil = null;
            _lastKey = null;
            Logger.Info("Pause ended");
        }

        var (active, since) = ScheduleMath.GetActive(_settings.Entries, now);
        var (next, at) = ScheduleMath.GetNext(_settings.Entries, now);
        ActiveEntry = active; ActiveSince = since; NextEntry = next; NextAt = at;

        // Temporary mode override ends at the next scheduled change (or if its entry was removed/disabled).
        if (OverrideEntry != null && (now >= OverrideUntil || !OverrideEntry.Enabled || !_settings.Entries.Contains(OverrideEntry)))
        {
            Logger.Info($"Override '{OverrideEntry.Name}' ended");
            OverrideEntry = null;
            _lastKey = null;
        }
        if (OverrideEntry != null)
        {
            if (!IsPaused)
            {
                var okey = "override:" + OverrideEntry.Id + "@" + _overrideSince.Ticks;
                bool enforceO = _settings.EnforceIntervalMinutes > 0 && now - _lastApply >= TimeSpan.FromMinutes(_settings.EnforceIntervalMinutes);
                if (okey != _lastKey || force || enforceO)
                {
                    bool nlForce = okey != _lastKey || user;
                    _lastKey = okey;
                    Logger.Info($"Applying override '{OverrideEntry.Name}'");
                    Apply(OverrideEntry, 0, nlForce);
                }
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!IsPaused && active != null)
        {
            var key = active.Id + "@" + since.Ticks;
            bool changed = key != _lastKey;
            bool enforce = _settings.EnforceIntervalMinutes > 0 && now - _lastApply >= TimeSpan.FromMinutes(_settings.EnforceIntervalMinutes);
            if (changed || force || enforce)
            {
                // Fade only for a "live" schedule boundary, not on startup / wake / manual re-apply.
                bool live = changed && !force && _lastKey != null && now - since < TimeSpan.FromMinutes(2);
                int transition = live ? _settings.TransitionSeconds : 0;
                if (changed && _lastKey != null && !force) EntryStarted?.Invoke(active);
                _lastKey = key;
                Logger.Info($"Applying '{active.Name}' (changed={changed}, force={force}, enforce={enforce}, fade={transition}s)");
                Apply(active, transition, changed || user);
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-applies the active entry immediately.</summary>
    public void ApplyNow() => Evaluate(force: true, user: true);

    /// <summary>Applies an arbitrary entry right away (preview from the editor).</summary>
    public void Preview(ScheduleEntry entry) => Apply(entry, 0, true);

    /// <summary>
    /// Switch to another mode right now. It stays until the next scheduled change,
    /// then the schedule takes over again. Choosing the scheduled entry returns to the schedule.
    /// </summary>
    public void SetOverride(ScheduleEntry entry)
    {
        PausedUntil = null;
        if (ReferenceEquals(entry, ActiveEntry))
        {
            ClearOverride();
            return;
        }
        OverrideEntry = entry;
        _overrideSince = DateTime.Now;
        OverrideUntil = NextEntry != null ? NextAt : DateTime.MaxValue;
        Logger.Info($"Override '{entry.Name}' until {OverrideUntil}");
        Evaluate(force: true, user: true);
    }

    public void ClearOverride()
    {
        if (OverrideEntry != null) Logger.Info("Override cleared");
        OverrideEntry = null;
        _lastKey = null;
        Evaluate(force: true, user: true);
    }

    public void Pause(TimeSpan? duration)
    {
        PausedUntil = duration.HasValue ? DateTime.Now + duration.Value : DateTime.MaxValue;
        Logger.Info($"Paused until {PausedUntil}");
        CancelTransition();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PauseUntilNext()
    {
        if (NextEntry == null) { Pause(null); return; }
        PausedUntil = NextAt;
        Logger.Info($"Paused until next change {PausedUntil}");
        CancelTransition();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        PausedUntil = null;
        _lastKey = null;
        Logger.Info("Resumed");
        Evaluate(force: true, user: true);
    }

    public void CancelTransition()
    {
        _cts?.Cancel();
    }

    private void Apply(ScheduleEntry entry, int transitionSeconds, bool forceNightLight)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _lastApply = DateTime.Now;

        // Snapshot the targets so the UI can keep editing while we work.
        var targets = entry.Targets.Select(t => new MonitorTarget
        {
            MonitorId = t.MonitorId, SetBrightness = t.SetBrightness, Brightness = t.Brightness,
            SetContrast = t.SetContrast, Contrast = t.Contrast,
        }).ToList();
        var snapshot = new ScheduleEntry
        {
            Id = entry.Id, Name = entry.Name, Targets = targets,
            NightLight = new NightLightTarget { Set = entry.NightLight.Set, Enabled = entry.NightLight.Enabled, Strength = entry.NightLight.Strength },
        };

        Task.Run(() =>
        {
            try
            {
                var applied = ApplyCore(snapshot, transitionSeconds, forceNightLight, cts.Token);
                _dispatcher.BeginInvoke(() => Applied?.Invoke(applied));
            }
            catch (Exception ex)
            {
                Logger.Error("Apply failed", ex);
            }
        });
    }

    private static Dictionary<string, (int B, int C)> ApplyCore(ScheduleEntry entry, int transitionSeconds, bool forceNightLight, CancellationToken ct)
    {
        var applied = new Dictionary<string, (int B, int C)>();
        lock (MonitorService.HardwareLock)
        {
            if (ct.IsCancellationRequested) return applied;
            using var session = DisplaySession.OpenUnlocked();

            var ops = new List<(string Id, DisplayProperty Prop, int From, int To)>();
            foreach (var m in session.Monitors)
            {
                var t = entry.FindTarget(m.Id);
                if (t == null) continue;
                if (t.SetBrightness)
                    ops.Add((m.Id, DisplayProperty.Brightness, transitionSeconds > 0 ? session.GetUnlocked(m.Id, DisplayProperty.Brightness) : -1, t.Brightness));
                if (t.SetContrast)
                    ops.Add((m.Id, DisplayProperty.Contrast, transitionSeconds > 0 ? session.GetUnlocked(m.Id, DisplayProperty.Contrast) : -1, t.Contrast));
            }

            void Record(string id, DisplayProperty p, int v)
            {
                applied.TryGetValue(id, out var cur);
                if (!applied.ContainsKey(id)) cur = (-1, -1);
                applied[id] = p == DisplayProperty.Brightness ? (v, cur.C) : (cur.B, v);
            }

            if (transitionSeconds <= 0 || ops.All(o => o.From < 0 || o.From == o.To))
            {
                foreach (var op in ops)
                    if (session.SetUnlocked(op.Id, op.Prop, op.To)) Record(op.Id, op.Prop, op.To);
            }
            else
            {
                int steps = Math.Clamp(transitionSeconds * 2, 2, 240);
                int interval = (int)(transitionSeconds * 1000.0 / steps);
                var last = ops.Select(o => o.From).ToArray();
                for (int k = 1; k <= steps; k++)
                {
                    for (int i = 0; i < ops.Count; i++)
                    {
                        var op = ops[i];
                        int v = op.From < 0
                            ? (k == steps ? op.To : -1)
                            : (int)Math.Round(op.From + (op.To - op.From) * (double)k / steps);
                        if (v < 0 || v == last[i]) continue;
                        if (session.SetUnlocked(op.Id, op.Prop, v)) { last[i] = v; Record(op.Id, op.Prop, v); }
                    }
                    if (k < steps && ct.WaitHandle.WaitOne(interval)) break;
                }
            }
        }

        if (entry.NightLight.Set && !ct.IsCancellationRequested)
        {
            var nl = entry.NightLight;
            var last = NightLightService.Get();
            bool upToDate = last.Known && last.Enabled == nl.Enabled && (!nl.Enabled || last.Strength == nl.Strength);
            // Opening Settings flashes a window, so skip it on wake/unlock re-applies when nothing changed.
            if (forceNightLight || !upToDate)
                NightLightService.Set(nl.Enabled, nl.Enabled ? nl.Strength : null);
        }
        return applied;
    }

    // ---------- system events ----------
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) ScheduleEventReapply(TimeSpan.FromSeconds(6), displaysChanged: true);
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.SessionLogon)
            ScheduleEventReapply(TimeSpan.FromSeconds(3), displaysChanged: false);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => ScheduleEventReapply(TimeSpan.FromSeconds(4), displaysChanged: true);

    private void OnTimeChanged(object? sender, EventArgs e)
        => _dispatcher.BeginInvoke(() => Evaluate());

    private void ScheduleEventReapply(TimeSpan delay, bool displaysChanged)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _displaysChangedPending |= displaysChanged;
            _eventDelay.Stop();
            _eventDelay.Interval = delay;
            _eventDelay.Start();
        });
    }

    private void OnEventDelayElapsed(object? sender, EventArgs e)
    {
        _eventDelay.Stop();
        if (_displaysChangedPending)
        {
            _displaysChangedPending = false;
            DisplaysChanged?.Invoke();
        }
        if (_settings.ReapplyOnSystemEvents && !IsPaused)
        {
            Logger.Info("System event: re-applying active entry");
            Evaluate(force: true);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _eventDelay.Stop();
        _cts?.Cancel();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
    }
}
