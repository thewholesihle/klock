using Klock.Core;

namespace Klock.Tests;

public class FramePacingTests
{
    // refresh (Hz), cap (fps), expected sync interval, resulting fps
    [Theory]
    [InlineData(60, 0, 1)]     // uncapped: every refresh
    [InlineData(144, 0, 1)]
    [InlineData(60, 60, 1)]    // cap equals refresh
    [InlineData(59.94, 60, 1)] // NTSC-ish refresh must not halve the rate
    [InlineData(60, 30, 2)]
    [InlineData(120, 60, 2)]
    [InlineData(144, 60, 3)]   // 48 fps: even cadence, never above the cap
    [InlineData(144, 30, 5)]   // clamped to MaxSyncInterval (4)... see next test
    [InlineData(165, 60, 3)]
    [InlineData(240, 120, 2)]
    public void Sync_interval_never_exceeds_the_cap(double hz, int cap, int expectedUnclamped)
    {
        int n = FramePacing.SyncInterval(1000.0 / hz, cap);
        Assert.Equal(Math.Min(expectedUnclamped, FramePacing.MaxSyncInterval), n);
        if (cap > 0 && n < FramePacing.MaxSyncInterval) Assert.True(hz / n <= cap + 0.5, $"{hz}/{n} exceeds cap {cap}");
    }

    [Fact]
    public void Unknown_refresh_is_treated_as_every_refresh() => Assert.Equal(1, FramePacing.SyncInterval(0, 30));

    [Fact]
    public void Median_refresh_divides_out_the_sync_interval_and_ignores_outliers()
    {
        var samples = new List<double> { 13.9, 13.9, 14.0, 13.9, 90.0, 13.8, 13.9 }; // 2 refreshes of 6.94 ms + a hitch
        Assert.Equal(6.95, FramePacing.MedianRefreshMs(samples, 2), 2);
    }

    [Theory]
    [InlineData(16.7, true)]
    [InlineData(6.9, true)]
    [InlineData(1.0, false)] // present returned immediately: not pacing
    public void Detects_presents_that_do_not_block(double ms, bool paces) => Assert.Equal(paces, FramePacing.PresentPaces(ms));
}
