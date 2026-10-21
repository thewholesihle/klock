using Klock.Core.Settings;

namespace Klock.Core.Themes;

public static class ThemeResolver
{
    /// <summary>
    /// Decide whether the night theme is active.
    /// </summary>
    /// <param name="appsUseLightTheme">Windows "app mode" (null = unknown, treated as light).</param>
    /// <param name="utcOffsetHours">Local UTC offset, needed only for the solar schedule.</param>
    public static bool IsNight(KlockSettings s, DateTime localNow, bool? appsUseLightTheme, double utcOffsetHours)
    {
        switch (s.Theme)
        {
            case ThemeMode.Day: return false;
            case ThemeMode.Night: return true;
            case ThemeMode.Auto: return appsUseLightTheme == false;
            default:
                var (start, end) = ScheduleWindow(s, localNow, utcOffsetHours);
                return InWindow(localNow.TimeOfDay, start, end);
        }
    }

    /// <summary>Night begins at <c>start</c> and ends at <c>end</c> (may wrap midnight).</summary>
    public static (TimeSpan Start, TimeSpan End) ScheduleWindow(KlockSettings s, DateTime localNow, double utcOffsetHours)
    {
        if (s.Schedule == ScheduleKind.Solar && s.Latitude is double lat && s.Longitude is double lon
            && SolarCalc.Compute(localNow.Date, lat, lon, utcOffsetHours) is { } sun)
        {
            return (sun.Sunset, sun.Sunrise);
        }

        // Custom times, also the fallback for polar day/night and missing coordinates.
        TimeSpan.TryParse(s.NightStart, out var a);
        TimeSpan.TryParse(s.NightEnd, out var b);
        return (a, b);
    }

    public static bool InWindow(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        if (start == end) return false;
        return start < end ? now >= start && now < end : now >= start || now < end;
    }
}
