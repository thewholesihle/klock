namespace Klock.Core;

/// <summary>
/// Chooses how to hit a frame-rate cap. Presenting every Nth vertical blank gives a perfectly even
/// cadence (a software sleep to 60 fps on a 144 Hz panel would alternate 2- and 3-refresh frames and judder).
/// </summary>
public static class FramePacing
{
    public const int MaxSyncInterval = 4;

    /// <summary>
    /// Sync interval N so that refreshHz / N never exceeds <paramref name="capFps"/>.
    /// Returns 1 when uncapped or when the refresh rate is unknown.
    /// </summary>
    public static int SyncInterval(double refreshMs, int capFps)
    {
        if (capFps <= 0 || refreshMs <= 0) return 1;
        double refreshHz = 1000.0 / refreshMs;
        int n = (int)Math.Ceiling(refreshHz / capFps - 0.05); // 0.05 tolerance: 60.0 Hz vs a 60 cap -> 1
        return Math.Clamp(n, 1, MaxSyncInterval);
    }

    /// <summary>Median of samples (the frame interval divided by the sync interval used for it).</summary>
    public static double MedianRefreshMs(IReadOnlyList<double> intervalsMs, int syncIntervalUsed)
    {
        if (intervalsMs.Count == 0) return 0;
        var sorted = intervalsMs.Select(x => x / Math.Max(1, syncIntervalUsed)).OrderBy(x => x).ToArray();
        return sorted[sorted.Length / 2];
    }

    /// <summary>
    /// A present that returns immediately (software rendering, remote sessions) does not pace frames.
    /// Below ~4 ms per frame we assume that and fall back to a timed sleep.
    /// </summary>
    public static bool PresentPaces(double refreshMs) => refreshMs >= 4.0;
}
