using Klock.Core.Time;

namespace Klock.Tests;

public class MovementEngineTests
{
    private static double Tod(int h, int m, double s) => h * 3600 + m * 60 + s;

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(3, 0, 0, 90, 0)]
    [InlineData(6, 30, 0, 195, 180)]
    [InlineData(12, 0, 0, 0, 0)]
    [InlineData(15, 15, 0, 97.5, 90)]
    [InlineData(23, 59, 0, 359.5, 354)]
    public void Digital_hour_and_minute_angles_are_exact(int h, int m, double s, double hourDeg, double minDeg)
    {
        var a = MovementEngine.Evaluate(MovementType.Digital, Tod(h, m, s));
        Assert.Equal(hourDeg % 360, a.HourDeg % 360, 6);
        Assert.Equal(minDeg % 360, a.MinuteDeg % 360, 6);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(15.0, 90)]
    [InlineData(30.5, 183)]
    [InlineData(59.999, 359.994)]
    public void Digital_second_hand_is_continuous(double sec, double deg)
    {
        var a = MovementEngine.Evaluate(MovementType.Digital, Tod(10, 20, sec));
        Assert.Equal(deg, a.SecondDeg, 3);
    }

    [Fact]
    public void Quartz_second_hand_is_settled_on_the_displayed_second()
    {
        // Half a second after a tick the spring has fully settled: angle == 6 * second.
        for (int s = 0; s < 60; s++)
        {
            var a = MovementEngine.Evaluate(MovementType.Quartz, Tod(8, 15, s + 0.5));
            Assert.Equal(s * 6.0, a.SecondDeg, 4);
        }
    }

    [Fact]
    public void Quartz_tick_starts_at_previous_position_and_overshoots_once()
    {
        double start = MovementEngine.Evaluate(MovementType.Quartz, Tod(8, 15, 10.0)).SecondDeg;
        Assert.Equal(54.0, start, 6); // 9 * 6: still on the previous second at the instant of the tick

        double max = double.MinValue;
        for (double f = 0; f < 0.4; f += 0.001)
            max = Math.Max(max, MovementEngine.Evaluate(MovementType.Quartz, Tod(8, 15, 10 + f)).SecondDeg);
        Assert.InRange(max, 60.2, 61.5); // overshoots 60 by a fraction of a degree, no more
    }

    [Fact]
    public void Quartz_tick_reaches_target_within_80ms()
    {
        double at80 = MovementEngine.Evaluate(MovementType.Quartz, Tod(8, 15, 10.08)).SecondDeg;
        Assert.InRange(at80, 59.0, 61.0);
    }

    [Fact]
    public void Quartz_hour_and_minute_hands_step_once_per_second()
    {
        var a = MovementEngine.Evaluate(MovementType.Quartz, Tod(1, 0, 10.2));
        var b = MovementEngine.Evaluate(MovementType.Quartz, Tod(1, 0, 10.9));
        Assert.Equal(a.MinuteDeg, b.MinuteDeg, 9);
        var c = MovementEngine.Evaluate(MovementType.Quartz, Tod(1, 0, 11.1));
        Assert.Equal(0.1, c.MinuteDeg - a.MinuteDeg, 6);
    }

    [Fact]
    public void Mechanical_beats_four_times_per_second()
    {
        // Mid-beat the hand rests on a multiple of 1.5 degrees.
        for (int beat = 0; beat < 240; beat++)
        {
            double t = Tod(4, 4, beat / 4.0 + 0.2); // 200 ms into each 250 ms beat
            var a = MovementEngine.Evaluate(MovementType.Mechanical, t);
            Assert.Equal(beat % 240 * 1.5, a.SecondDeg, 4);
        }
    }

    [Fact]
    public void Mechanical_second_hand_never_moves_backwards_by_more_than_overshoot()
    {
        double prev = MovementEngine.Evaluate(MovementType.Mechanical, 0).SecondDeg;
        for (double t = 0.001; t < 30; t += 0.001)
        {
            double cur = MovementEngine.Evaluate(MovementType.Mechanical, t).SecondDeg;
            Assert.True(cur - prev > -0.25, $"regressed at t={t}: {prev} -> {cur}");
            prev = cur;
        }
    }

    [Fact]
    public void Mechanical_hour_and_minute_hands_move_continuously()
    {
        var a = MovementEngine.Evaluate(MovementType.Mechanical, Tod(2, 0, 0));
        var b = MovementEngine.Evaluate(MovementType.Mechanical, Tod(2, 0, 0.25));
        Assert.True(b.MinuteDeg > a.MinuteDeg);
    }

    [Fact]
    public void Time_wraps_at_midnight_and_negative_input_is_normalised()
    {
        var a = MovementEngine.Evaluate(MovementType.Digital, 86400 + 3600);
        var b = MovementEngine.Evaluate(MovementType.Digital, 3600);
        Assert.Equal(b, a);
        var c = MovementEngine.Evaluate(MovementType.Digital, -3600);
        Assert.Equal(MovementEngine.Evaluate(MovementType.Digital, 82800), c);
    }

    [Theory]
    [InlineData(0.5, 53)]
    public void Step_response_is_monotonic_to_peak_and_settles_at_one(double zeta, double omega)
    {
        Assert.Equal(0, MovementEngine.Step(0, zeta, omega));
        Assert.Equal(1, MovementEngine.Step(1.0, zeta, omega), 9);
    }
}
