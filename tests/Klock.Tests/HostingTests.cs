using Klock.Core.Hosting;
using Klock.Core.Settings;

namespace Klock.Tests;

public class HostingTests
{
    [Theory]
    [InlineData("/s", LaunchMode.Screensaver, 0)]
    [InlineData("/S", LaunchMode.Screensaver, 0)]
    [InlineData("-s", LaunchMode.Screensaver, 0)]
    [InlineData("/c", LaunchMode.Configure, 0)]
    [InlineData("/c:12345", LaunchMode.Configure, 12345)]
    [InlineData("/C:12345", LaunchMode.Configure, 12345)]
    [InlineData("/p:99", LaunchMode.PreviewPane, 99)]
    [InlineData("/P:99", LaunchMode.PreviewPane, 99)]
    [InlineData("-p:99", LaunchMode.PreviewPane, 99)]
    [InlineData("/a", LaunchMode.Ignore, 0)]
    [InlineData("--preview", LaunchMode.Demo, 0)]
    [InlineData("--capture", LaunchMode.Capture, 0)]
    public void Parses_single_token_forms(string arg, LaunchMode mode, long hwnd)
    {
        var a = LaunchArgs.Parse(new[] { arg });
        Assert.Equal(mode, a.Mode);
        Assert.Equal(hwnd, a.Hwnd);
    }

    [Fact]
    public void Parses_space_separated_preview_handle()
    {
        var a = LaunchArgs.Parse(new[] { "/p", "263876" });
        Assert.Equal(LaunchMode.PreviewPane, a.Mode);
        Assert.Equal(263876, a.Hwnd);
        Assert.Empty(a.Options);
    }

    [Fact]
    public void No_args_opens_settings() => Assert.Equal(LaunchMode.Configure, LaunchArgs.Parse(Array.Empty<string>()).Mode);

    [Fact]
    public void Preview_without_a_handle_is_ignored_rather_than_crashing() =>
        Assert.Equal(LaunchMode.Ignore, LaunchArgs.Parse(new[] { "/p" }).Mode);

    [Fact]
    public void Dev_switch_options_are_exposed()
    {
        var a = LaunchArgs.Parse(new[] { "--preview", "--fullscreen", "--dial", "ocean" });
        Assert.True(a.Has("--fullscreen"));
        Assert.Equal("ocean", a.Value("--dial"));
        Assert.Null(a.Value("--missing"));
    }

    [Fact]
    public void Garbage_is_ignored()
    {
        Assert.Equal(LaunchMode.Ignore, LaunchArgs.Parse(new[] { "/zzz" }).Mode);
        Assert.Equal(LaunchMode.Configure, LaunchArgs.Parse(new[] { "/" }).Mode);
    }

    // ---- ExitInputDetector ----

    [Fact]
    public void Input_during_grace_period_is_ignored()
    {
        var d = new ExitInputDetector();
        Assert.False(d.OnDiscreteInput(0.1));
        Assert.False(d.OnMouseMove(100, 100, 0.2));
        Assert.False(d.OnMouseMove(900, 900, 0.4));
        Assert.True(d.OnDiscreteInput(0.6));
    }

    [Fact]
    public void Mouse_jitter_below_threshold_does_not_exit()
    {
        var d = new ExitInputDetector(0.5, 10);
        d.OnMouseMove(500, 500, 0.1);
        Assert.False(d.OnMouseMove(503, 504, 1.0)); // 5 px
        Assert.False(d.OnMouseMove(500, 509, 1.1)); // 9 px
        Assert.True(d.OnMouseMove(500, 511, 1.2));  // 11 px
    }

    [Fact]
    public void Anchor_follows_the_cursor_during_grace_so_a_cursor_parked_elsewhere_does_not_trigger_exit()
    {
        var d = new ExitInputDetector(0.5, 10);
        d.OnMouseMove(10, 10, 0.1);
        d.OnMouseMove(1500, 800, 0.3); // shell moved it as the window appeared
        Assert.False(d.OnMouseMove(1502, 801, 0.7));
    }

    // ---- FrameRatePolicy ----

    [Theory]
    [InlineData(0, true, true, 30, 30)]   // uncapped + battery -> 30
    [InlineData(0, false, true, 30, 0)]   // uncapped + wall power -> vsync
    [InlineData(60, true, true, 30, 30)]
    [InlineData(24, true, true, 30, 24)]  // never raises a lower user cap
    [InlineData(0, true, false, 30, 0)]   // user opted out of battery saving
    public void Frame_cap_policy(int userCap, bool battery, bool reduce, int batteryCap, int expected)
    {
        var s = new KlockSettings { FpsCap = userCap, ReduceOnBattery = reduce, BatteryFpsCap = batteryCap };
        Assert.Equal(expected, FrameRatePolicy.EffectiveCap(s, battery));
    }
}
