using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BrightnessScheduler.Controls;
using BrightnessScheduler.Localization;
using BrightnessScheduler.Models;
using BrightnessScheduler.Services;

namespace BrightnessScheduler.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const string GitHubUrl = "https://github.com/devcol-main/BrightnessScheduler";

    private readonly AppSettings _settings;
    private readonly SchedulerService _scheduler;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _clock;
    private int _selectedPage;
    private bool _isDetecting;
    private IReadOnlyList<TimelineSegment> _segments = Array.Empty<TimelineSegment>();
    private double _nowMinute;

    public ObservableCollection<DisplayViewModel> Displays { get; } = new();
    public ObservableCollection<EntryViewModel> Entries { get; } = new();

    public ICommand ApplyNowCommand { get; }
    public ICommand Pause1hCommand { get; }
    public ICommand PauseNextCommand { get; }
    public ICommand PauseForeverCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand AddEntryCommand { get; }
    public ICommand RefreshDisplaysCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand UninstallCommand { get; }

    /// <summary>Raised after displays have been (re)detected.</summary>
    public event Action? DisplaysDetected;

    public MainViewModel(AppSettings settings, SchedulerService scheduler)
    {
        _settings = settings;
        _scheduler = scheduler;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => SaveNow();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => RefreshStatus();
        _clock.Start();

        ApplyNowCommand = new RelayCommand(() => _scheduler.ApplyNow());
        Pause1hCommand = new RelayCommand(() => _scheduler.Pause(TimeSpan.FromHours(1)));
        PauseNextCommand = new RelayCommand(() => _scheduler.PauseUntilNext());
        PauseForeverCommand = new RelayCommand(() => _scheduler.Pause(null));
        ResumeCommand = new RelayCommand(() => _scheduler.Resume());
        AddEntryCommand = new RelayCommand(AddEntry);
        RefreshDisplaysCommand = new RelayCommand(async () => await DetectDisplaysAsync(), () => !IsDetecting);
        OpenDataFolderCommand = new RelayCommand(() => OpenShell(SettingsService.DataFolder));
        OpenLogCommand = new RelayCommand(() => { if (File.Exists(Logger.LogPath)) OpenShell(Logger.LogPath); });
        OpenGitHubCommand = new RelayCommand(() => OpenShell(GitHubUrl));
        UninstallCommand = new RelayCommand(() =>
        {
            Flush();
            var exe = Environment.ProcessPath;
            if (exe != null) Process.Start(new ProcessStartInfo(exe, "--uninstall") { UseShellExecute = false });
        });

        _scheduler.StateChanged += (_, _) => RefreshStatus();
        _scheduler.Applied += OnApplied;
        _scheduler.DisplaysChanged += async () => await DetectDisplaysAsync();
        Loc.LanguageChanged += () =>
        {
            foreach (var e in Entries) e.RefreshLanguage();
            OnPropertyChanged(nameof(TransitionText));
            OnPropertyChanged(nameof(VersionText));
            RefreshStatus();
        };

        foreach (var e in _settings.Entries.OrderBy(e => e.StartTime))
            Entries.Add(new EntryViewModel(e, this));
    }

    // ---------------- navigation ----------------
    public int SelectedPage
    {
        get => _selectedPage;
        set => Set(ref _selectedPage, value);
    }

    // ---------------- displays ----------------
    public bool IsDetecting
    {
        get => _isDetecting;
        private set { if (Set(ref _isDetecting, value)) OnPropertyChanged(nameof(HasNoDisplays)); }
    }

    public bool HasNoDisplays => !IsDetecting && Displays.Count == 0;

    public async Task DetectDisplaysAsync()
    {
        if (IsDetecting) return;
        IsDetecting = true;
        try
        {
            var infos = await Task.Run(MonitorService.Detect);
            Displays.Clear();
            foreach (var info in infos.Where(i => i.SupportsBrightness || i.SupportsContrast))
                Displays.Add(new DisplayViewModel(info, _scheduler));

            if (_settings.Entries.Count == 0 && SettingsService.IsFirstRun)
                CreateDefaultEntries();

            EnsureTargets();
            SaveNow();
            DisplaysDetected?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error("Display detection failed", ex);
        }
        finally
        {
            IsDetecting = false;
            OnPropertyChanged(nameof(HasNoDisplays));
            RefreshStatus();
        }
    }

    private void CreateDefaultEntries()
    {
        var night = new ScheduleEntry { Name = Loc.T("DefaultNight"), Time = "22:00" };
        var day = new ScheduleEntry { Name = Loc.T("DefaultDay"), Time = "07:00" };
        foreach (var d in Displays)
        {
            night.Targets.Add(new MonitorTarget { MonitorId = d.Id, MonitorName = d.Name, SetBrightness = d.SupportsBrightness, Brightness = 30, Contrast = 50 });
            day.Targets.Add(new MonitorTarget { MonitorId = d.Id, MonitorName = d.Name, SetBrightness = d.SupportsBrightness, Brightness = 100, Contrast = 50 });
        }
        _settings.Entries.Add(day);
        _settings.Entries.Add(night);
        Entries.Add(new EntryViewModel(day, this));
        Entries.Add(new EntryViewModel(night, this));
        Logger.Info("Created default schedule");
    }

    /// <summary>Make sure every entry has a target row for every connected display.</summary>
    private void EnsureTargets()
    {
        var connectedIds = Displays.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            entry.Targets.Clear();
            foreach (var d in Displays)
            {
                var t = entry.Model.Targets.FirstOrDefault(x => string.Equals(x.MonitorId, d.Id, StringComparison.OrdinalIgnoreCase));
                if (t == null)
                {
                    // Same model on a different port / instance id? Migrate it.
                    t = entry.Model.Targets.FirstOrDefault(x => !connectedIds.Contains(x.MonitorId)
                        && string.Equals(MonitorService.ModelOf(x.MonitorId), d.Info.Model, StringComparison.OrdinalIgnoreCase));
                    if (t != null) t.MonitorId = d.Id;
                }
                if (t == null)
                {
                    t = new MonitorTarget
                    {
                        MonitorId = d.Id,
                        Brightness = d.Brightness > 0 ? d.Brightness : 100,
                        Contrast = d.Contrast > 0 ? d.Contrast : 50,
                    };
                    entry.Model.Targets.Add(t);
                }
                t.MonitorName = d.Name;
                entry.Targets.Add(new TargetViewModel(t, d, entry));
            }
            entry.TargetChanged();
        }
    }

    private void OnApplied(Dictionary<string, (int B, int C)> values)
    {
        foreach (var d in Displays)
            if (values.TryGetValue(d.Id, out var v)) d.UpdateFromApplied(v.B, v.C);
    }

    // ---------------- schedule ----------------
    public bool HasNoEntries => Entries.Count == 0;

    private void AddEntry()
    {
        var now = DateTime.Now;
        var e = new ScheduleEntry { Name = Loc.T("NewEntry"), Time = $"{now.Hour:00}:00" };
        foreach (var d in Displays)
            e.Targets.Add(new MonitorTarget { MonitorId = d.Id, MonitorName = d.Name, SetBrightness = d.SupportsBrightness, Brightness = Math.Max(d.Brightness, 0), Contrast = Math.Max(d.Contrast, 0) });
        _settings.Entries.Add(e);
        var vm = new EntryViewModel(e, this);
        Entries.Add(vm);
        EnsureTargets();
        OnPropertyChanged(nameof(HasNoEntries));
        MarkDirty();
    }

    internal void DeleteEntry(EntryViewModel vm)
    {
        var r = MessageBox.Show(Loc.F("DeleteConfirmFmt", vm.DisplayName), Loc.T("AppName"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (r != MessageBoxResult.OK) return;
        _settings.Entries.Remove(vm.Model);
        Entries.Remove(vm);
        OnPropertyChanged(nameof(HasNoEntries));
        MarkDirty();
    }

    internal void PreviewEntry(EntryViewModel vm) => _scheduler.Preview(vm.Model);

    public void MarkDirty()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        RefreshTimeline();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        SettingsService.Save(_settings);
        _scheduler.Evaluate();
    }

    public void Flush() { if (_saveTimer.IsEnabled) SaveNow(); }

    // ---------------- status ----------------
    public bool IsPaused => _scheduler.IsPaused;

    public string ActiveTitle => _scheduler.ActiveEntry is { } e
        ? (string.IsNullOrWhiteSpace(e.Name) ? Loc.T("Unnamed") : e.Name)
        : Loc.T("NoActive");

    public string ActiveSubtitle
    {
        get
        {
            if (_scheduler.IsPaused)
            {
                var until = _scheduler.PausedUntil!.Value;
                return until == DateTime.MaxValue ? Loc.T("PausedForever") : Loc.F("PausedUntilFmt", FormatWhen(until));
            }
            return _scheduler.ActiveEntry != null ? Loc.F("SinceFmt", FormatWhen(_scheduler.ActiveSince)) : "";
        }
    }

    public string NextText => _scheduler.NextEntry is { } n
        ? Loc.F("NextFmt", string.IsNullOrWhiteSpace(n.Name) ? Loc.T("Unnamed") : n.Name, FormatWhen(_scheduler.NextAt), Loc.Duration(_scheduler.NextAt - DateTime.Now))
        : Loc.T("NoNext");

    public double ActiveLevel => Entries.FirstOrDefault(e => ReferenceEquals(e.Model, _scheduler.ActiveEntry))?.Level ?? 1.0;

    /// <summary>Moon for dim entries, sun for bright ones, pause icon while paused.</summary>
    public string StatusGlyph => _scheduler.IsPaused ? "" : ActiveLevel < 0.6 ? "" : "";

    public IReadOnlyList<TimelineSegment> TimelineSegments
    {
        get => _segments;
        private set => Set(ref _segments, value);
    }

    public double NowMinute
    {
        get => _nowMinute;
        private set => Set(ref _nowMinute, value);
    }

    private static string FormatWhen(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return t.ToString("HH:mm");
        if (t.Date == today.AddDays(1) || t.Date == today.AddDays(-1) || (t - today).TotalDays < 7)
            return t.ToString("ddd HH:mm", Loc.Instance.Culture);
        return t.ToString("M/d HH:mm");
    }

    public void RefreshStatus()
    {
        foreach (var e in Entries) e.IsActive = ReferenceEquals(e.Model, _scheduler.ActiveEntry);
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(ActiveTitle));
        OnPropertyChanged(nameof(ActiveSubtitle));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(ActiveLevel));
        OnPropertyChanged(nameof(StatusGlyph));
        RefreshTimeline();
    }

    private void RefreshTimeline()
    {
        var now = DateTime.Now;
        NowMinute = now.TimeOfDay.TotalMinutes;
        var segs = ScheduleMath.GetDaySegments(_settings.Entries, now);
        TimelineSegments = segs.Select(s =>
        {
            var vm = Entries.FirstOrDefault(e => ReferenceEquals(e.Model, s.Entry));
            return new TimelineSegment(s.StartMin, s.EndMin, vm?.DisplayName ?? "", vm?.Level ?? -1);
        }).ToList();
    }

    // ---------------- settings ----------------
    public int LanguageIndex
    {
        get => _settings.Language switch { "ko" => 1, "en" => 2, _ => 0 };
        set
        {
            if (value < 0) return;
            var lang = value switch { 1 => "ko", 2 => "en", _ => "auto" };
            if (lang == _settings.Language) return;
            _settings.Language = lang;
            Loc.Instance.SetLanguage(lang);
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public int ThemeIndex
    {
        get => _settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        set
        {
            if (value < 0) return;
            var theme = value switch { 1 => "light", 2 => "dark", _ => "system" };
            if (theme == _settings.Theme) return;
            _settings.Theme = theme;
            App.ApplyTheme(theme);
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public bool RunAtStartup
    {
        get => _settings.RunAtStartup;
        set
        {
            if (value == _settings.RunAtStartup) return;
            _settings.RunAtStartup = value;
            StartupService.Apply(value);
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public int TransitionSeconds
    {
        get => _settings.TransitionSeconds;
        set
        {
            if (value == _settings.TransitionSeconds) return;
            _settings.TransitionSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransitionText));
            MarkDirty();
        }
    }

    public string TransitionText
    {
        get
        {
            int s = _settings.TransitionSeconds;
            if (s <= 0) return Loc.T("Instant");
            if (s < 60) return Loc.F("SecondsFmt", s);
            return s % 60 == 0 ? Loc.F("MinutesOnlyFmt", s / 60) : Loc.F("MinSecFmt", s / 60, s % 60);
        }
    }

    public bool ReapplyOnSystemEvents
    {
        get => _settings.ReapplyOnSystemEvents;
        set { if (value != _settings.ReapplyOnSystemEvents) { _settings.ReapplyOnSystemEvents = value; OnPropertyChanged(); MarkDirty(); } }
    }

    private static readonly int[] EnforceOptions = { 0, 5, 15, 30, 60 };

    public int EnforceIndex
    {
        get { int i = Array.IndexOf(EnforceOptions, _settings.EnforceIntervalMinutes); return i < 0 ? 0 : i; }
        set
        {
            if (value < 0) return; // combo is being repopulated
            int v = EnforceOptions[Math.Clamp(value, 0, EnforceOptions.Length - 1)];
            if (v == _settings.EnforceIntervalMinutes) return;
            _settings.EnforceIntervalMinutes = v;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public bool ShowNotifications
    {
        get => _settings.ShowNotifications;
        set { if (value != _settings.ShowNotifications) { _settings.ShowNotifications = value; OnPropertyChanged(); MarkDirty(); } }
    }

    public string DataFolder => SettingsService.DataFolder;

    public string VersionText => Loc.F("VersionFmt",
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    private static void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Warn($"Failed to open {target}: {ex.Message}"); }
    }
}
