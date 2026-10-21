namespace Klock.Core.Themes;

/// <summary>
/// Animates the day/night blend factor (0 = day, 1 = night) so theme switches never hard-cut.
/// The first update snaps to the target so launching at night does not fade in from day.
/// </summary>
public sealed class ThemeFader
{
    public const double DefaultDuration = 1.5;

    private readonly double _duration;
    private double _progress;
    private bool _initialised;

    public ThemeFader(double durationSeconds = DefaultDuration) => _duration = Math.Max(0.01, durationSeconds);

    /// <summary>Eased blend factor 0..1.</summary>
    public double Amount => SmoothStep(_progress);

    public bool IsAnimating { get; private set; }

    public double Update(double deltaSeconds, bool night)
    {
        double target = night ? 1.0 : 0.0;
        if (!_initialised)
        {
            _progress = target;
            _initialised = true;
            IsAnimating = false;
            return Amount;
        }

        double step = Math.Max(0, deltaSeconds) / _duration;
        if (_progress < target) _progress = Math.Min(target, _progress + step);
        else if (_progress > target) _progress = Math.Max(target, _progress - step);
        IsAnimating = _progress != target;
        return Amount;
    }

    public static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
