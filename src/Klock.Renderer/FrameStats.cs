namespace Klock.Renderer;

/// <summary>Rolling present-to-present timing, used to verify smoothness and in the diagnostics overlay/log.</summary>
public sealed class FrameStats
{
    private const int Capacity = 600;
    private readonly double[] _ring = new double[Capacity];
    private int _count, _head;
    private readonly object _gate = new();
    public long TotalFrames { get; private set; }

    public void Add(double ms)
    {
        lock (_gate)
        {
            _ring[_head] = ms;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
            TotalFrames++;
        }
    }

    public readonly record struct Snapshot(int Samples, double MeanMs, double P50Ms, double P99Ms, double MaxMs, double Fps);

    public Snapshot Get()
    {
        double[] copy;
        lock (_gate) copy = _ring.Take(_count).ToArray();
        if (copy.Length == 0) return default;
        Array.Sort(copy);
        double mean = copy.Average();
        return new Snapshot(copy.Length, mean, copy[copy.Length / 2], copy[(int)(copy.Length * 0.99) >= copy.Length ? copy.Length - 1 : (int)(copy.Length * 0.99)],
            copy[^1], mean > 0 ? 1000.0 / mean : 0);
    }

    public override string ToString()
    {
        var s = Get();
        return $"{s.Samples} frames: mean {s.MeanMs:F2} ms ({s.Fps:F1} fps), p50 {s.P50Ms:F2}, p99 {s.P99Ms:F2}, max {s.MaxMs:F2}";
    }
}
