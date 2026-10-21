using System.Diagnostics;

namespace Klock.Core.Time;

/// <summary>
/// Phase-locks a monotonic (QueryPerformanceCounter-backed) timeline to the wall clock.
/// Small drift is slewed away invisibly; large jumps (DST, manual change, resume from sleep,
/// timezone change) snap straight to the new time.
/// </summary>
public sealed class SmoothClock
{
    public const double Day = 86400.0;

    private readonly Func<double> _wall;  // local seconds-of-day
    private readonly Func<double> _mono;  // monotonic seconds

    private double _anchorWall;
    private double _anchorMono;
    private double _lastSync;
    private double _lastNow;
    private double _slewRemaining;

    /// <summary>Seconds between wall-clock comparisons.</summary>
    public double ResyncInterval { get; set; } = 5.0;
    /// <summary>Errors below this are ignored (wall-clock read granularity).</summary>
    public double Deadband { get; set; } = 0.02;
    /// <summary>Errors above this snap instead of slew.</summary>
    public double SnapThreshold { get; set; } = 0.5;
    /// <summary>Maximum slew rate in seconds-per-second (2% = imperceptible).</summary>
    public double MaxSlewRate { get; set; } = 0.02;

    public SmoothClock(Func<double> wallSecondsOfDay, Func<double> monotonicSeconds)
    {
        _wall = wallSecondsOfDay;
        _mono = monotonicSeconds;
        SnapLocked();
    }

    public static SmoothClock CreateSystem()
    {
        long start = Stopwatch.GetTimestamp();
        return new SmoothClock(
            () => DateTime.Now.TimeOfDay.TotalSeconds,
            () => Stopwatch.GetElapsedTime(start).TotalSeconds);
    }

    // One clock is shared by every monitor's render thread so ticks stay in lock-step.
    private readonly object _gate = new();

    /// <summary>Force an immediate hard resync (call on power resume / time-change events).</summary>
    public void Snap()
    {
        lock (_gate) SnapLocked();
    }

    private void SnapLocked()
    {
        double m = _mono();
        _anchorWall = _wall();
        _anchorMono = m;
        _lastSync = m;
        _lastNow = m;
        _slewRemaining = 0;
    }

    /// <summary>Current local seconds-of-day, in [0, 86400).</summary>
    public double Now()
    {
        lock (_gate) return NowLocked();
    }

    private double NowLocked()
    {
        double m = _mono();
        if (m - _lastSync >= ResyncInterval) Resync(m);

        // Cap the step so a long gap between frames (paused rendering) can't apply a visible lump of slew.
        double dt = Math.Min(m - _lastNow, 0.25);
        _lastNow = m;
        if (_slewRemaining != 0 && dt > 0)
        {
            double max = MaxSlewRate * dt;
            double take = Math.Clamp(_slewRemaining, -max, max);
            _anchorWall += take;
            _slewRemaining -= take;
        }

        double t = (_anchorWall + (m - _anchorMono)) % Day;
        return t < 0 ? t + Day : t;
    }

    private void Resync(double m)
    {
        _lastSync = m;
        double predicted = _anchorWall + (m - _anchorMono) + _slewRemaining;
        double err = WrapDiff(_wall(), predicted);
        double abs = Math.Abs(err);
        if (abs > SnapThreshold)
        {
            _anchorWall = _wall();
            _anchorMono = m;
            _slewRemaining = 0;
        }
        else if (abs > Deadband)
        {
            _slewRemaining += err;
        }
    }

    /// <summary>Signed shortest difference a-b on a 24h circle.</summary>
    public static double WrapDiff(double a, double b)
    {
        double d = (a - b) % Day;
        if (d > Day / 2) d -= Day;
        else if (d < -Day / 2) d += Day;
        return d;
    }
}
