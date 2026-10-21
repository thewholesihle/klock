using Klock.Core.Burnin;
using Klock.Core.Settings;
using Klock.Core.Themes;
using Klock.Core.Time;

namespace Klock.Core;

/// <summary>
/// Turns settings + time into <see cref="ClockFrame"/>s. Shared by the screensaver, the settings
/// preview and the capture tool so every surface behaves identically.
/// </summary>
public sealed class ClockController
{
    public const double ClassicFraction = 0.70;
    public const double CompactFraction = 0.475;
    private const double SizeAnimSeconds = 0.8;

    private readonly SmoothClock _clock;
    private readonly Func<bool?> _appsUseLightTheme;
    private readonly Func<DateTime> _now;
    private readonly Func<double> _mono;
    private readonly ThemeFader _fader = new();

    private KlockSettings _settings;
    private double _lastMono = double.NaN;
    private double _size;
    private bool _sizeInit;
    private bool _night;
    private double _nightCheckedAt = double.NegativeInfinity;

    /// <param name="appsUseLightTheme">Reads the Windows app-theme (null when unknown).</param>
    public ClockController(KlockSettings settings, SmoothClock? clock = null, Func<bool?>? appsUseLightTheme = null,
        Func<DateTime>? now = null, Func<double>? monotonicSeconds = null)
    {
        _settings = settings;
        _clock = clock ?? SmoothClock.CreateSystem();
        _appsUseLightTheme = appsUseLightTheme ?? (() => null);
        _now = now ?? (() => DateTime.Now);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _mono = monotonicSeconds ?? (() => sw.Elapsed.TotalSeconds);
    }

    public KlockSettings Settings => _settings;

    /// <summary>Apply new settings live. Dial/lume/movement take effect immediately; size and theme animate.</summary>
    public void Apply(KlockSettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
            _nightCheckedAt = double.NegativeInfinity; // re-evaluate theme on the next frame
        }
    }

    // Apply() arrives from the UI thread while Update() runs on a render thread.
    private readonly object _gate = new();

    /// <summary>Re-sync to the wall clock (resume from sleep, time zone change, ...).</summary>
    public void ResyncTime() => _clock.Snap();

    public static double TargetSize(DialSize size) => size == DialSize.Compact ? CompactFraction : ClassicFraction;

    public ClockFrame Update(MonitorOffset? monitorOffset = null, double shortEdgePx = 1080)
    {
        lock (_gate) return UpdateLocked(monitorOffset, shortEdgePx);
    }

    private ClockFrame UpdateLocked(MonitorOffset? monitorOffset, double shortEdgePx)
    {
        double mono = _mono();
        double dt = double.IsNaN(_lastMono) ? 0 : Math.Clamp(mono - _lastMono, 0, 0.25);
        _lastMono = mono;

        // Theme is polled at ~1 Hz; the fader smooths the visual change.
        if (mono - _nightCheckedAt >= 1.0)
        {
            _nightCheckedAt = mono;
            var local = _now();
            double offsetH = TimeZoneInfo.Local.GetUtcOffset(local).TotalHours;
            _night = ThemeResolver.IsNight(_settings, local, _appsUseLightTheme(), offsetH);
        }
        double night = _fader.Update(dt, _night);

        // Size eases (smoothstep-flavoured exponential) toward the target.
        double target = TargetSize(_settings.Size);
        if (!_sizeInit) { _size = target; _sizeInit = true; }
        else
        {
            double k = 1 - Math.Exp(-dt * (4.0 / SizeAnimSeconds));
            _size += (target - _size) * k;
            if (Math.Abs(target - _size) < 1e-4) _size = target;
        }

        var hands = MovementEngine.Evaluate(_settings.Movement, _clock.Now());
        var burn = PixelShift.Evaluate(mono, shortEdgePx, _settings.BurnIn);
        Rgba.TryFromHex(_settings.BackgroundHex, out var bg);

        return new ClockFrame
        {
            Dial = DialCatalog.ById(_settings.DialId),
            Lume = LumeCatalog.ById(_settings.LumeId),
            Night = night,
            Hands = hands,
            BurnIn = burn,
            SizeFraction = _size,
            Background = bg,
            Numerals = _settings.Numerals,
            MatchDialBackground = _settings.Background == BackgroundMode.MatchDial,
            OffsetX = monitorOffset?.X ?? 0,
            OffsetY = monitorOffset?.Y ?? 0,
        };
    }

    /// <summary>True while any animation is running that needs full-rate frames.</summary>
    public bool IsAnimatingTheme => _fader.IsAnimating;
}


