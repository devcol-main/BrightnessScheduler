// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using BrightnessScheduler.Localization;
using BrightnessScheduler.Models;

namespace BrightnessScheduler.ViewModels;

public sealed class EntryViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _isActive;

    public ScheduleEntry Model { get; }
    public ObservableCollection<DayViewModel> Days { get; } = new();
    public ObservableCollection<TargetViewModel> Targets { get; } = new();

    public ICommand PreviewCommand { get; }
    public ICommand DeleteCommand { get; }

    public EntryViewModel(ScheduleEntry model, MainViewModel main)
    {
        Model = model;
        _main = main;
        // Monday-first order
        foreach (var d in new[] { 1, 2, 3, 4, 5, 6, 0 })
            Days.Add(new DayViewModel(d, this));
        PreviewCommand = new RelayCommand(() => _main.PreviewEntry(this));
        DeleteCommand = new RelayCommand(() => _main.DeleteEntry(this));
    }

    public string Name
    {
        get => Model.Name;
        set { if (Model.Name != value) { Model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); _main.MarkDirty(); } }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Model.Name) ? Loc.T("Unnamed") : Model.Name;

    public bool Enabled
    {
        get => Model.Enabled;
        set { if (Model.Enabled != value) { Model.Enabled = value; OnPropertyChanged(); _main.MarkDirty(); } }
    }

    public static IReadOnlyList<int> HourOptions { get; } = Enumerable.Range(0, 24).ToList();
    public static IReadOnlyList<int> MinuteOptions { get; } = Enumerable.Range(0, 60).ToList();

    public int Hour
    {
        get => Model.StartTime.Hours;
        set { SetTime(value, Minute); OnPropertyChanged(); }
    }

    public int Minute
    {
        get => Model.StartTime.Minutes;
        set { SetTime(Hour, value); OnPropertyChanged(); }
    }

    private void SetTime(int h, int m)
    {
        var t = $"{Math.Clamp(h, 0, 23):00}:{Math.Clamp(m, 0, 59):00}";
        if (Model.Time == t) return;
        Model.Time = t;
        OnPropertyChanged(nameof(TimeText));
        _main.MarkDirty();
    }

    public string TimeText => Model.Time;

    // ----- Night Light -----
    public bool NightLightSet
    {
        get => Model.NightLight.Set;
        set { if (Model.NightLight.Set != value) { Model.NightLight.Set = value; OnPropertyChanged(); TargetChanged(); } }
    }

    public bool NightLightOn
    {
        get => Model.NightLight.Enabled;
        set { if (Model.NightLight.Enabled != value) { Model.NightLight.Enabled = value; OnPropertyChanged(); TargetChanged(); } }
    }

    public int NightLightStrength
    {
        get => Model.NightLight.Strength;
        set { if (Model.NightLight.Strength != value) { Model.NightLight.Strength = value; OnPropertyChanged(); TargetChanged(); } }
    }

    /// <summary>Highlight the entry currently in effect.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    /// <summary>0..1 "how bright" this entry is, used for colors and icons.</summary>
    public double Level
    {
        get
        {
            var values = new List<int>();
            foreach (var t in Targets)
            {
                if (t.SetBrightness && t.SupportsBrightness) values.Add(t.Brightness);
                if (t.SetContrast && t.SupportsContrast) values.Add(t.Contrast);
            }
            return values.Count == 0 ? 0.5 : values.Average() / 100.0;
        }
    }

    internal void DaysChanged()
    {
        Model.Days = Days.Where(d => d.IsChecked).Select(d => d.DayOfWeek).OrderBy(d => d).ToList();
        _main.MarkDirty();
    }

    internal void TargetChanged()
    {
        OnPropertyChanged(nameof(Level));
        _main.MarkDirty();
    }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        foreach (var d in Days) d.RefreshLanguage();
    }
}

public sealed class DayViewModel : ObservableObject
{
    private readonly EntryViewModel _owner;
    public int DayOfWeek { get; }

    public DayViewModel(int dayOfWeek, EntryViewModel owner)
    {
        DayOfWeek = dayOfWeek;
        _owner = owner;
    }

    public string Label => Loc.DayShort(DayOfWeek);

    public bool IsChecked
    {
        get => _owner.Model.Days.Contains(DayOfWeek);
        set
        {
            if (value == IsChecked) return;
            if (value) _owner.Model.Days.Add(DayOfWeek); else _owner.Model.Days.Remove(DayOfWeek);
            OnPropertyChanged();
            _owner.DaysChanged();
        }
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}

/// <summary>One display's settings inside a schedule entry.</summary>
public sealed class TargetViewModel : ObservableObject
{
    private readonly EntryViewModel _owner;
    public MonitorTarget Model { get; }
    public DisplayViewModel Display { get; }

    public TargetViewModel(MonitorTarget model, DisplayViewModel display, EntryViewModel owner)
    {
        Model = model;
        Display = display;
        _owner = owner;
    }

    public bool SupportsBrightness => Display.SupportsBrightness;
    public bool SupportsContrast => Display.SupportsContrast;

    public bool SetBrightness
    {
        get => Model.SetBrightness && SupportsBrightness;
        set { if (Model.SetBrightness != value) { Model.SetBrightness = value; OnPropertyChanged(); _owner.TargetChanged(); } }
    }

    public int Brightness
    {
        get => Model.Brightness;
        set { if (Model.Brightness != value) { Model.Brightness = value; OnPropertyChanged(); _owner.TargetChanged(); } }
    }

    public bool SetContrast
    {
        get => Model.SetContrast && SupportsContrast;
        set { if (Model.SetContrast != value) { Model.SetContrast = value; OnPropertyChanged(); _owner.TargetChanged(); } }
    }

    public int Contrast
    {
        get => Model.Contrast;
        set { if (Model.Contrast != value) { Model.Contrast = value; OnPropertyChanged(); _owner.TargetChanged(); } }
    }
}
