using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using BrightnessScheduler.Localization;
using BrightnessScheduler.Services;

namespace BrightnessScheduler.ViewModels;

/// <summary>A connected display with live (manual) brightness / contrast control.</summary>
public sealed class DisplayViewModel : ObservableObject
{
    private readonly SchedulerService _scheduler;
    private readonly DispatcherTimer _debounce;
    private bool _pendingB, _pendingC;
    private int _brightness, _contrast;

    public DisplayInfo Info { get; }

    public DisplayViewModel(DisplayInfo info, SchedulerService scheduler)
    {
        Info = info;
        _scheduler = scheduler;
        _brightness = Math.Max(0, info.Brightness);
        _contrast = Math.Max(0, info.Contrast);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _debounce.Tick += (_, _) => Flush();
        Loc.LanguageChanged += () => { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Subtitle)); };
    }

    public string Id => Info.Id;

    public string Name =>
        !string.IsNullOrWhiteSpace(Info.FriendlyName) && !(Info.IsInternal && Info.FriendlyName.Contains("Generic", StringComparison.OrdinalIgnoreCase))
            ? Info.FriendlyName
            : Info.IsInternal ? Loc.T("BuiltInDisplay") : Info.Model;

    public string Subtitle
    {
        get
        {
            var method = Info.UsesWmi ? "WMI" : "DDC/CI";
            return Info.DisplayNumber > 0 ? $"{Loc.F("DisplayNumFmt", Info.DisplayNumber)} · {method}" : method;
        }
    }

    /// <summary>Segoe Fluent Icons glyph: laptop or monitor.</summary>
    public string Glyph => Info.IsInternal ? "" : "";

    public bool SupportsBrightness => Info.SupportsBrightness;
    public bool SupportsContrast => Info.SupportsContrast;

    public int Brightness
    {
        get => _brightness;
        set { if (Set(ref _brightness, value)) { _pendingB = true; Restart(); } }
    }

    public int Contrast
    {
        get => _contrast;
        set { if (Set(ref _contrast, value)) { _pendingC = true; Restart(); } }
    }

    /// <summary>Update from values written by the scheduler (no hardware write).</summary>
    public void UpdateFromApplied(int b, int c)
    {
        if (b >= 0 && !_pendingB) { _brightness = b; OnPropertyChanged(nameof(Brightness)); }
        if (c >= 0 && !_pendingC) { _contrast = c; OnPropertyChanged(nameof(Contrast)); }
    }

    private void Restart()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Flush()
    {
        _debounce.Stop();
        _scheduler.CancelTransition();
        bool doB = _pendingB && SupportsBrightness, doC = _pendingC && SupportsContrast;
        int b = _brightness, c = _contrast;
        _pendingB = _pendingC = false;
        var id = Id;
        Task.Run(() =>
        {
            if (doB) MonitorService.SetValue(id, DisplayProperty.Brightness, b);
            if (doC) MonitorService.SetValue(id, DisplayProperty.Contrast, c);
        });
    }
}
