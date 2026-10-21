using System.Diagnostics;
using System.Globalization;
using Klock.Core;
using Klock.Core.Burnin;
using Klock.Core.Hosting;
using Klock.Core.Settings;
using Klock.Core.Themes;
using Klock.Core.Time;
using Klock.Renderer;

namespace Klock.Screensaver;

/// <summary>Developer / marketing tooling: --capture, --sheet, --bench. Never used by Windows itself.</summary>
internal static class DevCommands
{
    public static int Capture(LaunchArgs a)
    {
        string path = a.Options.FirstOrDefault(x => !x.StartsWith("--")) ?? "capture.png";
        var (w, h) = ParseSize(a.Value("--size"), 1280, 800);
        var settings = SettingsFromArgs(a);
        double night = Num(a.Value("--night"), 0);
        double tod = TimeSpan.TryParse(a.Value("--time") ?? "10:08:35", CultureInfo.InvariantCulture, out var t)
            ? t.TotalSeconds : 10 * 3600 + 8 * 60 + 35;
        Rgba.TryFromHex(a.Value("--bg") ?? "#000000", out var bg);

        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            return CaptureGif(path, a, settings, w, h, night, tod, bg);

        var frame = new ClockFrame
        {
            Dial = DialCatalog.ById(settings.DialId),
            Lume = LumeCatalog.ById(settings.LumeId),
            Night = night,
            Hands = MovementEngine.Evaluate(settings.Movement, tod),
            BurnIn = PixelShift.Evaluate(0, Math.Min(w, h), BurnInLevel.Off),
            SizeFraction = ClockController.TargetSize(settings.Size),
            Background = bg,
            MatchDialBackground = !a.Has("--solid"),
            Numerals = settings.Numerals,
        };

        var sw = Stopwatch.StartNew();
        OffscreenRenderer.RenderPng(path, frame, w, h, forceSoftware: a.Has("--soft"));
        Console.WriteLine($"Wrote {path} ({w}x{h}) in {sw.ElapsedMilliseconds} ms");
        return 0;
    }


    /// <summary>Animated GIF of the real movement (e.g. a quartz tick), for marketing.</summary>
    private static int CaptureGif(string path, LaunchArgs a, KlockSettings settings, int w, int h, double night, double tod, Rgba bg)
    {
        double seconds = Num(a.Value("--seconds"), 3);
        int fps = (int)Num(a.Value("--fps"), 30);
        int count = Math.Max(1, (int)Math.Round(seconds * fps));
        var frames = Enumerable.Range(0, count).Select(i => new ClockFrame
        {
            Dial = DialCatalog.ById(settings.DialId),
            Lume = LumeCatalog.ById(settings.LumeId),
            Night = night,
            Hands = MovementEngine.Evaluate(settings.Movement, tod + i / (double)fps),
            BurnIn = PixelShift.Evaluate(0, Math.Min(w, h), BurnInLevel.Off),
            SizeFraction = ClockController.TargetSize(settings.Size),
            Background = bg,
            MatchDialBackground = !a.Has("--solid"),
            Numerals = settings.Numerals,
        });

        var sw = Stopwatch.StartNew();
        var rendered = OffscreenRenderer.RenderSequence(frames, w, h, a.Has("--soft")).ToList();
        GifWriter.Save(path, rendered, w, h, fps);
        Console.WriteLine($"Wrote {path} ({w}x{h}, {count} frames @ {fps} fps, {new FileInfo(path).Length / 1024} KB) in {sw.ElapsedMilliseconds} ms");
        return 0;
    }

    /// <summary>--icon out.ico : the application icon, drawn by the clock renderer itself.</summary>
    public static int Icon(LaunchArgs a)
    {
        string path = a.Options.FirstOrDefault(x => !x.StartsWith("--")) ?? "klock.ico";
        var images = new List<(int, byte[])>();
        foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var frame = new ClockFrame
            {
                Dial = DialCatalog.ById(a.Value("--dial") ?? "signal-blue"),
                Lume = LumeCatalog.Default,
                Night = 0,
                Hands = MovementEngine.Evaluate(MovementType.Digital, 10 * 3600 + 9 * 60 + 36),
                BurnIn = PixelShift.Evaluate(0, size, BurnInLevel.Off),
                SizeFraction = 0.90,
                Background = Rgba.Transparent,
            };
            images.Add((size, OffscreenRenderer.RenderBgra(frame, size, size, a.Has("--soft"))));
        }
        IconWriter.Save(path, images);
        Console.WriteLine($"Wrote {path} ({images.Count} sizes)");
        return 0;
    }
    /// <summary>Contact sheet of every dial (4x4) at the given night amount.</summary>
    public static int Sheet(LaunchArgs a)
    {
        string path = a.Options.FirstOrDefault(x => !x.StartsWith("--")) ?? "sheet.png";
        double night = Num(a.Value("--night"), 0);
        string lume = a.Value("--lume") ?? "bgw9";
        var settings0 = SettingsFromArgs(a);
        const int tile = 420, cols = 4;
        int rows = (DialCatalog.All.Count + cols - 1) / cols;
        double tod = 10 * 3600 + 8 * 60 + 35;

        var frames = DialCatalog.All.Select(d => new ClockFrame
        {
            Dial = d,
            Lume = LumeCatalog.ById(lume),
            Night = night,
            Hands = MovementEngine.Evaluate(MovementType.Digital, tod),
            BurnIn = PixelShift.Evaluate(0, tile, BurnInLevel.Off),
            SizeFraction = 0.90,
            Background = Rgba.FromHex("#0A0A0A"),
            MatchDialBackground = true,
            Numerals = ParseFont(a.Value("--font")) ?? settings0.Numerals,
        });

        var sheet = new byte[tile * cols * tile * rows * 4];
        int idx = 0;
        foreach (var px in OffscreenRenderer.RenderSequence(frames, tile, tile, a.Has("--soft")))
        {
            int tx = idx % cols * tile, ty = idx / cols * tile;
            for (int y = 0; y < tile; y++)
                Buffer.BlockCopy(px, y * tile * 4, sheet, ((ty + y) * tile * cols + tx) * 4, tile * 4);
            idx++;
        }
        PngWriter.Save(path, sheet, tile * cols, tile * rows);
        Console.WriteLine($"Wrote {path}");
        return 0;
    }

    /// <summary>Run a real window for N seconds and report frame timing, CPU and memory.</summary>
    public static int Bench(LaunchArgs a)
    {
        double seconds = Num(a.Value("--seconds"), 6);
        var settings = SettingsFromArgs(a);
        var host = new ClockHost(ClockHost.Kind.Demo, settings, fullscreenDemo: a.Has("--fullscreen"), forceSoftware: a.Has("--soft"));

        var proc = Process.GetCurrentProcess();
        var cpu0 = proc.TotalProcessorTime;
        var wall = Stopwatch.StartNew();

        // Sample memory once a second so growth (a leak) is distinguishable from steady state.
        var mem = new List<string>();
        using var cts = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { break; }
                proc.Refresh();
                mem.Add($"{wall.Elapsed.TotalSeconds,4:F0}s ws {proc.WorkingSet64 / 1048576.0,5:F0} MB  private {proc.PrivateMemorySize64 / 1048576.0,5:F0} MB  " +
                        $"managed {GC.GetTotalMemory(false) / 1048576.0,5:F1} MB  gc0/1/2 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
            }
        });
        host.Run(seconds);
        cts.Cancel();
        sampler.Wait(2000);
        wall.Stop();
        foreach (var line in mem) Console.WriteLine("  " + line);
        proc.Refresh();
        double cpuPct = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;

        foreach (var v in host.Views)
        {
            Console.WriteLine($"adapter: {v.AdapterName}{(v.IsSoftware ? " (software)" : "")}");
            Console.WriteLine($"frames: {v.Stats}");
            Console.WriteLine($"refresh: {(v.RefreshMs > 0 ? 1000.0 / v.RefreshMs : 0):F1} Hz measured, presenting every {v.SyncInterval} refresh(es)");
        }
        Console.WriteLine($"cpu: {cpuPct:F2}% of {Environment.ProcessorCount} logical cores over {wall.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"memory: working set {proc.WorkingSet64 / 1048576.0:F0} MB, private {proc.PrivateMemorySize64 / 1048576.0:F0} MB");
        return 0;
    }

    private static KlockSettings SettingsFromArgs(LaunchArgs a)
    {
        var s = new SettingsStore().Load();
        if (a.Value("--dial") is { } d) s.DialId = d;
        if (a.Value("--lume") is { } l) s.LumeId = l;
        if (Enum.TryParse<MovementType>(a.Value("--move"), true, out var mv)) s.Movement = mv;
        if (a.Has("--compact")) s.Size = DialSize.Compact;
        if (a.Has("--classic")) s.Size = DialSize.Classic;
        if (a.Has("--night-mode")) s.Theme = ThemeMode.Night;
        if (a.Has("--day-mode")) s.Theme = ThemeMode.Day;
        if (Enum.TryParse<BurnInLevel>(a.Value("--burnin"), true, out var b)) s.BurnIn = b;
        if (int.TryParse(a.Value("--fps"), out var fps)) s.FpsCap = fps;
        if (ParseFont(a.Value("--font")) is { } font) s.Numerals = font;
        return s.Normalize();
    }

    /// <summary>--font panchang | clash | satoshi | melodrama | klock</summary>
    private static NumeralFont? ParseFont(string? v) => v?.ToLowerInvariant() switch
    {
        "panchang" => NumeralFont.Panchang,
        "clash" or "clashdisplay" => NumeralFont.ClashDisplay,
        "satoshi" => NumeralFont.Satoshi,
        "melodrama" => NumeralFont.Melodrama,
        "klock" or "builtin" => NumeralFont.Klock,
        _ => null,
    };

    private static double Num(string? s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static (int, int) ParseSize(string? s, int dw, int dh)
    {
        var p = s?.Split('x');
        return p is { Length: 2 } && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h) ? (w, h) : (dw, dh);
    }
}



