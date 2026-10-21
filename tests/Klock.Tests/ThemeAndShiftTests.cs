using Klock.Core.Burnin;
using Klock.Core.Settings;
using Klock.Core.Themes;

namespace Klock.Tests;

public class ThemeAndShiftTests
{
    [Fact]
    public void Catalog_has_16_unique_dials_and_14_unique_lumes()
    {
        Assert.Equal(16, DialCatalog.All.Count);
        Assert.Equal(16, DialCatalog.All.Select(d => d.Id).Distinct().Count());
        Assert.Equal(14, LumeCatalog.All.Count);
        Assert.Equal(14, LumeCatalog.All.Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public void Every_dial_has_legible_numerals_and_a_distinct_hand_tone()
    {
        foreach (var d in DialCatalog.All)
        {
            var face = Rgba.Lerp(d.FaceCenter, d.FaceEdge, 0.5f);
            float ink = Math.Abs(face.Luminance - d.Ink.Luminance);
            Assert.True(ink > 0.35f, $"{d.Id}: ink/face luminance delta {ink:F2}");
            // Hands and pills are pale on every dial (light-tinted white), never darker than the face by much.
            Assert.True(d.Hand.Luminance > 0.85f, $"{d.Id}: hands should be pale, luminance {d.Hand.Luminance:F2}");
        }
    }

    [Fact]
    public void Unknown_ids_fall_back_to_defaults()
    {
        Assert.Equal("white", DialCatalog.ById("nope").Id);
        Assert.Equal(LumeCatalog.Default, LumeCatalog.ById(null));
    }

    [Theory]
    [InlineData("#FF8800", 1f, 0.5333333f, 0f)]
    [InlineData("0f0", 0f, 1f, 0f)]
    public void Hex_parses(string hex, float r, float g, float b)
    {
        var c = Rgba.FromHex(hex);
        Assert.Equal(r, c.R, 3);
        Assert.Equal(g, c.G, 3);
        Assert.Equal(b, c.B, 3);
    }

    [Fact]
    public void Hex_roundtrips() => Assert.Equal("#1D1D1F", Rgba.FromHex("#1D1D1F").ToHex());

    [Fact]
    public void Bad_hex_is_rejected_without_throwing_via_TryFromHex()
    {
        Assert.False(Rgba.TryFromHex("banana", out _));
        Assert.False(Rgba.TryFromHex(null, out _));
    }

    // ---- ThemeResolver ----

    private static KlockSettings S(ThemeMode m) => new() { Theme = m };

    [Fact]
    public void Auto_follows_windows_app_theme()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        Assert.True(ThemeResolver.IsNight(S(ThemeMode.Auto), now, appsUseLightTheme: false, 0));
        Assert.False(ThemeResolver.IsNight(S(ThemeMode.Auto), now, appsUseLightTheme: true, 0));
        Assert.False(ThemeResolver.IsNight(S(ThemeMode.Auto), now, appsUseLightTheme: null, 0));
    }

    [Fact]
    public void Forced_modes_ignore_everything_else()
    {
        var now = new DateTime(2026, 1, 1, 3, 0, 0);
        Assert.True(ThemeResolver.IsNight(S(ThemeMode.Night), now, true, 0));
        Assert.False(ThemeResolver.IsNight(S(ThemeMode.Day), now, false, 0));
    }

    [Theory]
    [InlineData(18, 59, false)]
    [InlineData(19, 0, true)]
    [InlineData(23, 30, true)]
    [InlineData(2, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    public void Custom_schedule_wraps_midnight(int h, int m, bool night)
    {
        var s = new KlockSettings { Theme = ThemeMode.Schedule, NightStart = "19:00", NightEnd = "07:00" };
        Assert.Equal(night, ThemeResolver.IsNight(s, new DateTime(2026, 3, 1, h, m, 0), null, 0));
    }

    [Fact]
    public void Solar_schedule_london_equinox_is_about_6_and_18()
    {
        var sun = SolarCalc.Compute(new DateTime(2026, 3, 20), 51.5, -0.12, 0)!.Value;
        Assert.InRange(sun.Sunrise.TotalHours, 5.8, 6.4);
        Assert.InRange(sun.Sunset.TotalHours, 17.9, 18.6);
    }

    [Fact]
    public void Solar_polar_night_returns_null_and_schedule_falls_back_to_custom()
    {
        Assert.Null(SolarCalc.Compute(new DateTime(2026, 12, 21), 80, 0, 0));
        var s = new KlockSettings
        {
            Theme = ThemeMode.Schedule, Schedule = ScheduleKind.Solar, Latitude = 80, Longitude = 0,
            NightStart = "20:00", NightEnd = "06:00",
        };
        Assert.True(ThemeResolver.IsNight(s, new DateTime(2026, 12, 21, 21, 0, 0), null, 0));
        Assert.False(ThemeResolver.IsNight(s, new DateTime(2026, 12, 21, 12, 0, 0), null, 0));
    }

    [Fact]
    public void Fader_snaps_on_first_update_then_eases_over_1_5s()
    {
        var f = new ThemeFader();
        Assert.Equal(1.0, f.Update(0.016, night: true)); // launched at night: no fade-in from day
        f.Update(0.016, night: false);
        Assert.True(f.IsAnimating);
        Assert.InRange(f.Amount, 0.9, 1.0);
        for (int i = 0; i < 100; i++) f.Update(0.016, false);
        Assert.Equal(0.0, f.Amount);
        Assert.False(f.IsAnimating);
    }

    [Fact]
    public void Fader_takes_about_1_5_seconds()
    {
        var f = new ThemeFader();
        f.Update(0, false);
        double t = 0;
        while (f.Update(0.01, true) < 1.0) t += 0.01;
        Assert.InRange(t, 1.45, 1.55);
    }

    // ---- PixelShift ----

    [Fact]
    public void Shift_is_zero_when_off()
    {
        var s = PixelShift.Evaluate(1234, 1080, BurnInLevel.Off);
        Assert.Equal(new BurnInState(0, 0, 1, 1), s);
    }

    [Theory]
    [InlineData(BurnInLevel.Standard, 0.010)]
    [InlineData(BurnInLevel.Extended, 0.020)]
    public void Shift_stays_within_amplitude_and_is_imperceptibly_slow(BurnInLevel level, double frac)
    {
        double edge = 1080, limit = edge * frac * 1.0001, maxSpeed = 0;
        double px = 0, py = 0;
        bool first = true;
        for (double t = 0; t < 3 * 3600; t += 1.0)
        {
            var s = PixelShift.Evaluate(t, edge, level);
            Assert.InRange(Math.Abs(s.X), 0, limit);
            Assert.InRange(Math.Abs(s.Y), 0, limit);
            Assert.InRange(s.HighlightScale, 0.9, 1.0);
            if (!first) maxSpeed = Math.Max(maxSpeed, Math.Sqrt((s.X - px) * (s.X - px) + (s.Y - py) * (s.Y - py)));
            (px, py, first) = (s.X, s.Y, false);
        }
        Assert.True(maxSpeed < 0.5, $"drift {maxSpeed:F3}px/s would be visible");
    }

    [Fact]
    public void Shift_covers_a_meaningful_area_over_an_hour()
    {
        double minX = 0, maxX = 0, minY = 0, maxY = 0;
        for (double t = 0; t < 3600; t += 5)
        {
            var s = PixelShift.Evaluate(t, 1080, BurnInLevel.Standard);
            minX = Math.Min(minX, s.X); maxX = Math.Max(maxX, s.X);
            minY = Math.Min(minY, s.Y); maxY = Math.Max(maxY, s.Y);
        }
        Assert.True(maxX - minX > 10 && maxY - minY > 10);
    }
}
