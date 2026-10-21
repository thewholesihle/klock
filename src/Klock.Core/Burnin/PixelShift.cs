namespace Klock.Core.Burnin;

public enum BurnInLevel { Off, Standard, Extended }

/// <summary>Per-frame burn-in protection output.</summary>
/// <param name="X">Horizontal offset in pixels.</param>
/// <param name="Y">Vertical offset in pixels.</param>
/// <param name="HighlightScale">Multiplier (&lt;= 1) for static highlights, slowly breathing.</param>
/// <param name="LumeCap">Peak lume intensity cap (1 = uncapped).</param>
public readonly record struct BurnInState(double X, double Y, double HighlightScale, double LumeCap);

public static class PixelShift
{
    // Non-resonant periods (seconds) so the path never retraces a short loop.
    private const double PeriodX = 421.0;
    private const double PeriodY = 307.0;
    private const double PeriodX2 = 1181.0;
    private const double PeriodY2 = 887.0;
    private const double BreathPeriod = 97.0;

    public static double AmplitudeFraction(BurnInLevel level) => level switch
    {
        BurnInLevel.Standard => 0.010,
        BurnInLevel.Extended => 0.020,
        _ => 0.0,
    };

    /// <param name="timeSeconds">Any monotonically increasing time (seconds).</param>
    /// <param name="shortEdgePx">Short edge of the display in pixels.</param>
    public static BurnInState Evaluate(double timeSeconds, double shortEdgePx, BurnInLevel level)
    {
        if (level == BurnInLevel.Off) return new BurnInState(0, 0, 1, 1);

        double a = AmplitudeFraction(level) * shortEdgePx;
        const double tau = Math.PI * 2;
        // Lissajous with a slow secondary term so the traced area fills in over hours.
        double x = a * (0.8 * Math.Sin(tau * timeSeconds / PeriodX) + 0.2 * Math.Sin(tau * timeSeconds / PeriodX2 + 1.3));
        double y = a * (0.8 * Math.Sin(tau * timeSeconds / PeriodY + 0.9) + 0.2 * Math.Cos(tau * timeSeconds / PeriodY2));

        double depth = level == BurnInLevel.Extended ? 0.08 : 0.04;
        double breath = 1.0 - depth * (0.5 + 0.5 * Math.Sin(tau * timeSeconds / BreathPeriod));
        double lumeCap = level == BurnInLevel.Extended ? 0.70 : 0.85;
        return new BurnInState(x, y, breath, lumeCap);
    }
}
