using System;
using System.Collections.Generic;
using System.Linq;
using BrightnessScheduler.Models;

namespace BrightnessScheduler.Services;

public static class ScheduleMath
{
    /// <summary>The entry in effect at <paramref name="at"/> and the moment it started.</summary>
    public static (ScheduleEntry? Entry, DateTime Since) GetActive(IEnumerable<ScheduleEntry> entries, DateTime at)
    {
        var list = entries.Where(e => e.Enabled && e.Days.Count > 0).ToList();
        for (int d = 0; d <= 7; d++)
        {
            var date = at.Date.AddDays(-d);
            var best = list
                .Where(e => e.Days.Contains((int)date.DayOfWeek))
                .Select(e => (Entry: e, Start: date + e.StartTime))
                .Where(x => x.Start <= at)
                .OrderByDescending(x => x.Start)
                .FirstOrDefault();
            if (best.Entry != null) return (best.Entry, best.Start);
        }
        return (null, DateTime.MinValue);
    }

    /// <summary>The next entry start strictly after <paramref name="after"/> that changes the active entry.</summary>
    public static (ScheduleEntry? Entry, DateTime At) GetNext(IEnumerable<ScheduleEntry> entries, DateTime after)
    {
        var list = entries.Where(e => e.Enabled && e.Days.Count > 0).ToList();
        var current = GetActive(list, after).Entry;
        for (int d = 0; d <= 8; d++)
        {
            var date = after.Date.AddDays(d);
            var starts = list
                .Where(e => e.Days.Contains((int)date.DayOfWeek))
                .Select(e => (Entry: e, Start: date + e.StartTime))
                .Where(x => x.Start > after)
                .OrderBy(x => x.Start);
            foreach (var s in starts)
                if (!ReferenceEquals(s.Entry, current)) return s;
        }
        return (null, DateTime.MaxValue);
    }

    /// <summary>Segments of a given day (minutes 0..1440) for the timeline view.</summary>
    public static List<(int StartMin, int EndMin, ScheduleEntry? Entry)> GetDaySegments(IEnumerable<ScheduleEntry> entries, DateTime day)
    {
        var list = entries.Where(e => e.Enabled && e.Days.Count > 0).ToList();
        var result = new List<(int, int, ScheduleEntry?)>();
        var date = day.Date;
        var starts = list
            .Where(e => e.Days.Contains((int)date.DayOfWeek))
            .Select(e => (int)e.StartTime.TotalMinutes)
            .Where(m => m > 0)
            .Distinct()
            .OrderBy(m => m)
            .ToList();
        var points = new List<int> { 0 };
        points.AddRange(starts);
        for (int i = 0; i < points.Count; i++)
        {
            int s = points[i];
            int e = i + 1 < points.Count ? points[i + 1] : 1440;
            var active = GetActive(list, date.AddMinutes(s)).Entry;
            if (result.Count > 0 && ReferenceEquals(result[^1].Item3, active))
                result[^1] = (result[^1].Item1, e, active);
            else
                result.Add((s, e, active));
        }
        return result;
    }
}
