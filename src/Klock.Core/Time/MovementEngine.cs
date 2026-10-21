namespace Klock.Core.Time;

public enum MovementType
{
    /// <summary>1 Hz tick with a damped-spring overshoot.</summary>
    Quartz,
    /// <summary>4 Hz (28,800 bph) stepped sweep with micro-settle.</summary>
    Mechanical,
    /// <summary>Perfectly continuous sweep, interpolated every frame.</summary>
    Digital,
}

/// <summary>Hand angles in degrees clockwise from 12 o'clock. Not wrapped to 0..360.</summary>
public readonly record struct HandAngles(double HourDeg, double MinuteDeg, double SecondDeg);

/// <summary>
/// Pure function of local time-of-day. Because the output depends only on the input time, hands are
/// exact to the displayed second by construction and the engine is trivially testable.
/// </summary>
public static class MovementEngine
{
    // Underdamped second-order step response parameters. peak time tp = pi / wd.
    // Quartz: tp ~ 70 ms, ~10% overshoot of a 6 degree step (0.6 deg), settles by ~150 ms.
    private const double QuartzZeta = 0.55;
    private const double QuartzOmega = 54.0;
    // Mechanical beat: tp ~ 35 ms, tiny overshoot on a 1.5 degree step.
    private const double MechZeta = 0.62;
    private const double MechOmega = 110.0;

    public const double MechanicalBeatsPerSecond = 4.0;

    public static HandAngles Evaluate(MovementType type, double secondsOfDay)
    {
        double tod = secondsOfDay % 86400.0;
        if (tod < 0) tod += 86400.0;

        double sec = tod % 60.0;
        switch (type)
        {
            case MovementType.Quartz:
            {
                double whole = Math.Floor(tod);
                double n = Math.Floor(sec);
                double f = sec - n;
                double second = (n - 1) * 6.0 + 6.0 * Step(f, QuartzZeta, QuartzOmega);
                // Quartz minute/hour hands advance in discrete one-second increments.
                return new HandAngles(HourAngle(whole), MinuteAngle(whole), second);
            }
            case MovementType.Mechanical:
            {
                double beat = Math.Floor(sec * MechanicalBeatsPerSecond);
                double elapsed = sec - beat / MechanicalBeatsPerSecond;
                double step = 6.0 / MechanicalBeatsPerSecond;
                double second = (beat - 1) * step + step * Step(elapsed, MechZeta, MechOmega);
                return new HandAngles(HourAngle(tod), MinuteAngle(tod), second);
            }
            default:
                return new HandAngles(HourAngle(tod), MinuteAngle(tod), sec * 6.0);
        }
    }

    public static double HourAngle(double tod) => (tod / 3600.0 % 12.0) * 30.0;
    public static double MinuteAngle(double tod) => (tod / 60.0 % 60.0) * 6.0;

    /// <summary>Unit step response of a second-order system, 0 at t=0 and 1 at rest.</summary>
    public static double Step(double t, double zeta, double omega)
    {
        if (t <= 0) return 0;
        double wd = omega * Math.Sqrt(1 - zeta * zeta);
        double e = Math.Exp(-zeta * omega * t);
        return 1 - e * (Math.Cos(wd * t) + zeta * omega / wd * Math.Sin(wd * t));
    }

    /// <summary>Seconds-of-day for a DateTime (local).</summary>
    public static double SecondsOfDay(DateTime local) => local.TimeOfDay.TotalSeconds;
}
