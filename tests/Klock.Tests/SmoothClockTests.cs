using Klock.Core.Time;

namespace Klock.Tests;

public class SmoothClockTests
{
    private sealed class Fake
    {
        public double Mono;
        public double Wall = 43200;
        public SmoothClock Clock;
        public Fake() => Clock = new SmoothClock(() => Wall, () => Mono);
        public void Advance(double s, bool wallFollows = true)
        {
            Mono += s;
            if (wallFollows) Wall += s;
        }
    }

    [Fact]
    public void Tracks_monotonic_time_between_resyncs()
    {
        var f = new Fake();
        f.Advance(3);
        Assert.Equal(43203, f.Clock.Now(), 6);
    }

    [Fact]
    public void Ignores_wall_clock_noise_inside_deadband()
    {
        var f = new Fake();
        f.Advance(6);
        f.Wall += 0.01; // 10 ms of read jitter
        Assert.Equal(43206, f.Clock.Now(), 6);
    }

    [Fact]
    public void Snaps_on_large_jump_like_dst_or_manual_change()
    {
        var f = new Fake();
        f.Advance(6);
        f.Wall += 3600; // clocks go forward an hour
        Assert.Equal(f.Wall, f.Clock.Now(), 6);
    }

    [Fact]
    public void Slews_small_drift_gradually()
    {
        var f = new Fake();
        f.Advance(5.01);
        f.Wall += 0.2;
        double first = f.Clock.Now();
        // 0.2 s error, never applied in a single step.
        Assert.True(first < f.Wall - 0.15);
        // 20 s of frames at 2% slew = 0.4 s of capacity, enough to absorb the 0.2 s error.
        double maxStep = 0, prev = first;
        for (int i = 0; i < 200; i++)
        {
            f.Advance(0.1);
            double now = f.Clock.Now();
            maxStep = Math.Max(maxStep, now - prev - 0.1);
            prev = now;
        }
        Assert.Equal(f.Wall, prev, 2);
        Assert.True(maxStep <= 0.1 * 0.02 + 1e-9, $"slew exceeded 2%: {maxStep}");
    }

    [Fact]
    public void Snap_method_forces_resync()
    {
        var f = new Fake();
        f.Advance(1);
        f.Wall += 10;
        f.Clock.Snap();
        Assert.Equal(f.Wall, f.Clock.Now(), 6);
    }

    [Fact]
    public void Handles_midnight_wrap()
    {
        var f = new Fake { Wall = 86399 };
        f.Clock = new SmoothClock(() => f.Wall % 86400, () => f.Mono);
        f.Advance(2);
        Assert.Equal(1, f.Clock.Now(), 6);
    }

    [Theory]
    [InlineData(10, 86390, 20)]
    [InlineData(86390, 10, -20)]
    [InlineData(100, 90, 10)]
    public void WrapDiff_takes_shortest_path(double a, double b, double expected) =>
        Assert.Equal(expected, SmoothClock.WrapDiff(a, b), 9);
}
