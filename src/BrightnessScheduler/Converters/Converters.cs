using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BrightnessScheduler.Controls;

namespace BrightnessScheduler.Converters;

/// <summary>int == ConverterParameter → true (two-way for RadioButtons).</summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && int.TryParse(parameter?.ToString(), out var p) && i == p;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && int.TryParse(parameter?.ToString(), out var p) ? p : Binding.DoNothing;
}

/// <summary>int == ConverterParameter → Visible.</summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && int.TryParse(parameter?.ToString(), out var p) && i == p ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Entry level (0..1) → accent brush matching the timeline colors.</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double level = value is double d ? d : 0.5;
        var brush = new SolidColorBrush(ScheduleTimeline.ColorFor(level));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
