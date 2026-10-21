using Klock.Core.Burnin;
using Klock.Core.Themes;
using Klock.Core.Time;

namespace Klock.Core;

/// <summary>Everything the renderer needs to paint one frame. Pure data, no behaviour.</summary>
public sealed record ClockFrame
{
    public required DialTheme Dial { get; init; }
    public required LumeColor Lume { get; init; }
    /// <summary>0 = full day, 1 = full night, eased.</summary>
    public required double Night { get; init; }
    public required HandAngles Hands { get; init; }
    public required BurnInState BurnIn { get; init; }
    /// <summary>Dial diameter as a fraction of the display's short edge (animated between Classic and Compact).</summary>
    public required double SizeFraction { get; init; }
    public required Rgba Background { get; init; }
    /// <summary>Typeface for the numerals.</summary>
    public NumeralFont Numerals { get; init; } = NumeralFont.Panchang;
    /// <summary>When true the renderer fills the screen with the dial's own colour and <see cref="Background"/> is ignored.</summary>
    public bool MatchDialBackground { get; init; }
    /// <summary>User per-monitor nudge, in fractions of the short edge.</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
}


