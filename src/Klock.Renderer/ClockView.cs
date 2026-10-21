using System.Diagnostics;
using Klock.Core;
using Klock.Core.Settings;

namespace Klock.Renderer;

/// <summary>
/// Renders a clock into any existing HWND (a screensaver monitor window, the Screen Saver Settings
/// preview pane, or a child window inside the settings app) on a dedicated thread. Present blocks to vsync so
/// each window paces to its own monitor, including mixed refresh rates; frame caps are applied by presenting
/// every Nth refresh so the cadence stays perfectly even.
/// </summary>
public sealed class ClockView : IDisposable
{
    /// <summary>Frames ignored by <see cref="Stats"/> and used to measure the refresh rate (cache build etc.).</summary>
    private const int WarmupFrames = 20;
    private const int RetuneEvery = 30;

    private readonly IntPtr _hwnd;
    private readonly ClockController _controller;
    private readonly Func<int> _fpsCap;
    private readonly Func<MonitorOffset?> _offset;
    private readonly Func<string?> _adapter;
    private readonly bool _forceSoftware;

    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _paused;
    private long _requestedSize; // packed (w << 32) | h
    private int _consecutiveLosses;

    public FrameStats Stats { get; } = new();
    public string AdapterName { get; private set; } = "";
    public bool IsSoftware { get; private set; }
    /// <summary>Measured display refresh period in ms (0 until measured).</summary>
    public double RefreshMs { get; private set; }
    public int SyncInterval { get; private set; } = 1;
    /// <summary>Raised on the render thread when rendering cannot continue (after recovery attempts).</summary>
    public event Action<Exception>? Failed;

    public ClockView(IntPtr hwnd, ClockController controller, Func<int>? fpsCap = null,
        Func<MonitorOffset?>? monitorOffset = null, Func<string?>? adapter = null, bool forceSoftware = false)
    {
        _hwnd = hwnd;
        _controller = controller;
        _fpsCap = fpsCap ?? (() => 0);
        _offset = monitorOffset ?? (() => null);
        _adapter = adapter ?? (() => null);
        _forceSoftware = forceSoftware;
        var (w, h) = Native.NativeMethods.GetClientSize(hwnd);
        Resize(w, h);
    }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { Name = "Klock render", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void SetPaused(bool paused) => _paused = paused;

    public void Resize(int width, int height) =>
        Interlocked.Exchange(ref _requestedSize, ((long)Math.Max(0, width) << 32) | (uint)Math.Max(0, height));

    public void Stop()
    {
        _stop = true;
        var t = _thread;
        _thread = null;
        if (t != null && t != Thread.CurrentThread && !t.Join(2000))
            Logger.Warn("Render thread did not stop within 2s");
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ render thread

    private void Run()
    {
        int hardFailures = 0;
        while (!_stop)
        {
            try
            {
                RenderSession();
                hardFailures = 0;
            }
            catch (Exception ex) when (!_stop)
            {
                Logger.Error("Render session failed", ex);
                if (++hardFailures >= 3)
                {
                    Failed?.Invoke(ex);
                    return;
                }
                Thread.Sleep(250 * hardFailures);
            }
        }
    }

    private RenderDevice CreateDevice()
    {
        try
        {
            return RenderDevice.Create(_adapter(), _forceSoftware);
        }
        catch (Exception ex)
        {
            Logger.Warn($"GPU device unavailable, falling back to WARP: {ex.Message}");
            return RenderDevice.Create(null, forceSoftware: true);
        }
    }

    private (int W, int H) RequestedSize()
    {
        long v = Interlocked.Read(ref _requestedSize);
        return ((int)(v >> 32), (int)(v & 0xFFFFFFFF));
    }

    private void RenderSession()
    {
        var (w, h) = RequestedSize();
        while (!_stop && (w <= 0 || h <= 0))
        {
            Thread.Sleep(50);
            (w, h) = RequestedSize();
        }
        if (_stop) return;

        using var dev = CreateDevice();
        AdapterName = dev.AdapterName;
        IsSoftware = dev.IsSoftware;
        Logger.Info($"Render device: {dev.AdapterName}{(dev.IsSoftware ? " (software)" : "")} {w}x{h}");

        using var dc = dev.CreateContext();
        using var painter = new ClockPainter(dev, dc);
        using var surface = new SwapChainSurface(dev, dc, _hwnd, w, h);

        var clock = Stopwatch.StartNew();
        double lastPresentEnd = clock.Elapsed.TotalMilliseconds;
        var sessionStart = clock.Elapsed;
        var samples = new List<double>(RetuneEvery);
        long frameNo = 0;
        int n = 1; // sync interval currently in use
        SyncInterval = 1;

        while (!_stop)
        {
            if (_paused)
            {
                Thread.Sleep(100);
                lastPresentEnd = clock.Elapsed.TotalMilliseconds;
                samples.Clear();
                continue;
            }

            var (rw, rh) = RequestedSize();
            if (rw <= 0 || rh <= 0) { Thread.Sleep(50); continue; }
            if (rw != surface.Width || rh != surface.Height)
            {
                surface.Resize(rw, rh);
                w = rw; h = rh;
                frameNo = 0; // re-warm after a resize (layer cache rebuild)
            }

            double frameStart = clock.Elapsed.TotalMilliseconds;
            var frame = _controller.Update(_offset(), Math.Min(w, h));
            painter.Paint(surface.Target, w, h, frame);
            var result = surface.Present(n);

            if (result == PresentResult.DeviceLost)
            {
                // Recreate everything unless this is happening in a tight loop.
                if (clock.Elapsed - sessionStart < TimeSpan.FromSeconds(2)) _consecutiveLosses++; else _consecutiveLosses = 1;
                Logger.Warn($"Device lost (#{_consecutiveLosses}); recreating");
                if (_consecutiveLosses > 4) throw new InvalidOperationException("GPU device repeatedly lost");
                return;
            }
            if (result == PresentResult.Occluded) Thread.Sleep(50);

            double now = clock.Elapsed.TotalMilliseconds;
            double interval = now - lastPresentEnd;
            lastPresentEnd = now;
            frameNo++;

            if (frameNo > WarmupFrames) Stats.Add(interval);

            // Learn the refresh period from the Present cadence, then choose the sync interval for the cap.
            if (frameNo > 3)
            {
                samples.Add(interval);
                if (samples.Count >= RetuneEvery)
                {
                    double refresh = FramePacing.MedianRefreshMs(samples, n);
                    samples.Clear();
                    if (refresh > 0)
                    {
                        RefreshMs = refresh;
                        int next = FramePacing.PresentPaces(refresh) ? FramePacing.SyncInterval(refresh, _fpsCap()) : 1;
                        if (next != n) { n = next; SyncInterval = n; }
                    }
                }
            }

            // Fallback for presents that do not block (software rendering, some remote sessions): timed sleep.
            if (RefreshMs > 0 && !FramePacing.PresentPaces(RefreshMs))
            {
                int cap = _fpsCap();
                if (cap <= 0) cap = 60;
                double budget = 1000.0 / cap - (now - frameStart);
                if (budget > 1.5) Thread.Sleep((int)(budget - 1.0));
            }
        }
    }
}
