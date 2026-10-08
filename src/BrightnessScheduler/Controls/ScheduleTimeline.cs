// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BrightnessScheduler.Controls;

/// <param name="Level">0..1 brightness of the entry, or -1 when no entry is active.</param>
public sealed record TimelineSegment(int StartMinute, int EndMinute, string Label, double Level);

/// <summary>24-hour bar showing which schedule entry is active when, plus a "now" marker.</summary>
public sealed class ScheduleTimeline : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<TimelineSegment>), typeof(ScheduleTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NowMinuteProperty = DependencyProperty.Register(
        nameof(NowMinute), typeof(double), typeof(ScheduleTimeline),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(ScheduleTimeline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush), typeof(Brush), typeof(ScheduleTimeline),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<TimelineSegment>? Segments
    {
        get => (IReadOnlyList<TimelineSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double NowMinute
    {
        get => (double)GetValue(NowMinuteProperty);
        set => SetValue(NowMinuteProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush MarkerBrush
    {
        get => (Brush)GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    private static readonly Color Night = Color.FromRgb(0x3F, 0x4A, 0xB8);
    private static readonly Color Day = Color.FromRgb(0xF6, 0xB9, 0x3B);
    private static readonly Color None = Color.FromRgb(0x9A, 0x9A, 0x9A);

    private const double BarTop = 8, BarHeight = 32, TickGap = 6;

    public ScheduleTimeline()
    {
        Height = 70;
        SnapsToDevicePixels = true;
    }

    public static Color ColorFor(double level)
    {
        if (level < 0) return None;
        level = Math.Clamp(level, 0, 1);
        // Ease so dim entries read clearly as "night".
        double t = Math.Pow(level, 1.6);
        byte L(byte a, byte b) => (byte)(a + (b - a) * t);
        return Color.FromRgb(L(Night.R, Day.R), L(Night.G, Day.G), L(Night.B, Day.B));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var tickFace = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var bar = new Rect(0, BarTop, w, BarHeight);
        dc.PushClip(new RectangleGeometry(bar, 8, 8));

        var segments = Segments;
        if (segments == null || segments.Count == 0)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, None.R, None.G, None.B)), null, bar);
        }
        else
        {
            foreach (var s in segments)
            {
                double x0 = w * s.StartMinute / 1440.0, x1 = w * s.EndMinute / 1440.0;
                var color = ColorFor(s.Level);
                var brush = new LinearGradientBrush(
                    Color.FromArgb(0xFF, color.R, color.G, color.B),
                    Color.FromArgb(0xD8, color.R, color.G, color.B), 90);
                dc.DrawRectangle(brush, null, new Rect(x0, BarTop, Math.Max(0, x1 - x0), BarHeight));

                // separator
                if (s.StartMinute > 0)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), null, new Rect(x0 - 0.5, BarTop, 1, BarHeight));

                if (!string.IsNullOrEmpty(s.Label))
                {
                    bool dark = s.Level >= 0.55;
                    var text = new FormattedText(s.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 12,
                        dark ? new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x05)) : Brushes.White, dpi);
                    if (text.Width + 12 < x1 - x0)
                        dc.DrawText(text, new Point(x0 + (x1 - x0 - text.Width) / 2, BarTop + (BarHeight - text.Height) / 2));
                }
            }
        }
        dc.Pop();

        // Hour ticks
        for (int h = 0; h <= 24; h += 3)
        {
            double x = w * h / 24.0;
            var label = new FormattedText($"{h:00}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tickFace, 11, Foreground, dpi);
            double tx = Math.Clamp(x - label.Width / 2, 0, w - label.Width);
            dc.DrawText(label, new Point(tx, BarTop + BarHeight + TickGap));
        }

        // Now marker
        double nx = Math.Clamp(w * NowMinute / 1440.0, 1, w - 1);
        var pen = new Pen(MarkerBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, new Point(nx, BarTop - 4), new Point(nx, BarTop + BarHeight + 2));
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(nx - 5, 0), true, true);
            g.LineTo(new Point(nx + 5, 0), true, false);
            g.LineTo(new Point(nx, BarTop - 2), true, false);
        }
        tri.Freeze();
        dc.DrawGeometry(MarkerBrush, null, tri);
    }
}
