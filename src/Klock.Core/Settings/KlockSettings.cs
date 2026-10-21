using Klock.Core.Burnin;
using Klock.Core.Time;

namespace Klock.Core.Settings;

public enum ThemeMode { Auto, Day, Night, Schedule }
public enum DialSize { Classic, Compact }
public enum MonitorMode { All, PrimaryOnly, Selected }
public enum ScheduleKind { CustomTimes, Solar }
public enum BackgroundMode { MatchDial, Solid }

/// <summary>Per-monitor nudge, as a fraction of that monitor's short edge (-0.25..0.25).</summary>
public sealed class MonitorOffset
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Everything the user can configure. Serialised as JSON; unknown/missing fields fall back to defaults.</summary>
public sealed class KlockSettings
{
    /// <summary>Bump when a release changes the meaning of stored values; older files are migrated on load.</summary>
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;

    // Look
    public string DialId { get; set; } = "white";
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;
    public string LumeId { get; set; } = "bgw9";
    public MovementType Movement { get; set; } = MovementType.Quartz;
    public DialSize Size { get; set; } = DialSize.Classic;
    public Themes.NumeralFont Numerals { get; set; } = Themes.NumeralFont.Panchang;
    /// <summary>MatchDial: the whole screen is the dial colour (full-bleed). Solid: <see cref="BackgroundHex"/> (black suits OLED).</summary>
    public BackgroundMode Background { get; set; } = BackgroundMode.MatchDial;
    public string BackgroundHex { get; set; } = "#000000";

    // Schedule (used when Theme == Schedule)
    public ScheduleKind Schedule { get; set; } = ScheduleKind.CustomTimes;
    public string NightStart { get; set; } = "19:00";
    public string NightEnd { get; set; } = "07:00";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Display
    public MonitorMode Monitors { get; set; } = MonitorMode.All;
    /// <summary>Device names (e.g. \\.\DISPLAY1) used when <see cref="Monitors"/> is Selected.</summary>
    public List<string> SelectedMonitors { get; set; } = new();
    public Dictionary<string, MonitorOffset> MonitorOffsets { get; set; } = new();

    // Performance
    /// <summary>0 = match display refresh rate.</summary>
    public int FpsCap { get; set; }
    public bool ReduceOnBattery { get; set; } = true;
    public int BatteryFpsCap { get; set; } = 30;
    /// <summary>Adapter description to prefer; null/empty = system default.</summary>
    public string? GpuAdapter { get; set; }

    // Protection / audio / UI
    public BurnInLevel BurnIn { get; set; } = BurnInLevel.Standard;
    public bool TickSound { get; set; }
    /// <summary>"auto" or a culture name such as "fr", "ar", "zh-Hans".</summary>
    public string Language { get; set; } = "auto";

    // Updates
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Clamp and repair values so downstream code can trust them.</summary>
    public KlockSettings Normalize()
    {
        if (!Enum.IsDefined(Theme)) Theme = ThemeMode.Auto;
        if (!Enum.IsDefined(Movement)) Movement = MovementType.Quartz;
        if (!Enum.IsDefined(Size)) Size = DialSize.Classic;
        if (!Enum.IsDefined(BurnIn)) BurnIn = BurnInLevel.Standard;
        if (!Enum.IsDefined(Monitors)) Monitors = MonitorMode.All;
        if (!Enum.IsDefined(Schedule)) Schedule = ScheduleKind.CustomTimes;
        if (!Enum.IsDefined(Background)) Background = BackgroundMode.MatchDial;
        if (!Enum.IsDefined(Numerals)) Numerals = Themes.NumeralFont.Panchang;
        if (!Themes.Rgba.TryFromHex(BackgroundHex, out _)) BackgroundHex = "#000000";
        if (FpsCap < 0) FpsCap = 0;
        if (FpsCap > 0) FpsCap = Math.Clamp(FpsCap, 10, 360);
        BatteryFpsCap = Math.Clamp(BatteryFpsCap, 10, 120);
        if (Latitude is < -90 or > 90) Latitude = null;
        if (Longitude is < -180 or > 180) Longitude = null;
        if (!TimeSpan.TryParse(NightStart, out _)) NightStart = "19:00";
        if (!TimeSpan.TryParse(NightEnd, out _)) NightEnd = "07:00";
        SelectedMonitors ??= new();
        MonitorOffsets ??= new();
        foreach (var o in MonitorOffsets.Values)
        {
            o.X = Math.Clamp(o.X, -0.25, 0.25);
            o.Y = Math.Clamp(o.Y, -0.25, 0.25);
        }
        return this;
    }

    /// <summary>
    /// Upgrade settings written by an older release. Stale look/behaviour values are reset to the current
    /// defaults, but the language and update-check timestamp always survive an upgrade.
    /// </summary>
    public KlockSettings MigrateToCurrent()
    {
        if (SchemaVersion >= CurrentSchema) return this;
        return new KlockSettings
        {
            Language = Language,
            LastUpdateCheckUtc = LastUpdateCheckUtc,
            SchemaVersion = CurrentSchema,
        }.Normalize();
    }

    public KlockSettings Clone() => SettingsJson.Deserialize(SettingsJson.Serialize(this));
}

