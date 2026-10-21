namespace Klock.Core.Themes;

/// <summary>The typeface used for the hour and minute numerals.</summary>
public enum NumeralFont
{
    /// <summary>Panchang Extrabold: wide, heavy and geometric.</summary>
    Panchang,
    /// <summary>Clash Display Semibold: sharp, modern display grotesque.</summary>
    ClashDisplay,
    /// <summary>Satoshi Black: a clean, confident geometric sans.</summary>
    Satoshi,
    /// <summary>Melodrama Bold: a high-contrast display serif.</summary>
    Melodrama,
    /// <summary>Klock's own extended monoline digits (built in; also the fallback if a font cannot be loaded).</summary>
    Klock,
}

public static class NumeralFontInfo
{
    public static string NameKey(NumeralFont f) => "font." + f.ToString().ToLowerInvariant();
}

