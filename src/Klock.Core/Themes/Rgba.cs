using System.Globalization;

namespace Klock.Core.Themes;

/// <summary>sRGB colour, components 0..1, non-premultiplied.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static Rgba Black => new(0, 0, 0, 1);
    public static Rgba White => new(1, 1, 1, 1);
    public static Rgba Transparent => new(0, 0, 0, 0);

    public static Rgba FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length is not (6 or 8)) throw new FormatException($"Bad colour '{hex}'");
        float Part(int i) => int.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        return new Rgba(Part(0), Part(2), Part(4), s.Length == 8 ? Part(6) : 1f);
    }

    public static bool TryFromHex(string? hex, out Rgba color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { color = FromHex(hex); return true; }
        catch (FormatException) { return false; }
    }

    public string ToHex()
    {
        static int Byte(float v) => (int)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
        return $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}";
    }

    public Rgba WithAlpha(float a) => this with { A = a };

    /// <summary>Linear blend in gamma space (what designers expect for dial colours).</summary>
    public static Rgba Lerp(Rgba a, Rgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }

    /// <summary>Scale RGB (not alpha) toward black.</summary>
    public Rgba Scale(float k) => new(R * k, G * k, B * k, A);

    public float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;
}
