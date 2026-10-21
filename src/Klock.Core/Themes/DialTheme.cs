namespace Klock.Core.Themes;

/// <summary>
/// A dial is pure data: adding one means adding a row to <see cref="DialCatalog"/>.
/// Day colours are specified directly; the night look is derived from <see cref="NightDim"/>
/// plus the selected lume colour.
/// </summary>
/// <param name="Id">Stable identifier stored in settings.</param>
/// <param name="FaceCenter">Radial gradient centre of the (full-bleed) dial.</param>
/// <param name="FaceEdge">Radial gradient edge.</param>
/// <param name="Ink">Numerals and minute ticks (a deep tone of the dial's own hue).</param>
/// <param name="Hand">Hands and the raised index pills (a pale, tinted white).</param>
/// <param name="Accent">The small dot at the centre of the cap.</param>
/// <param name="ShadowStrength">Cast shadow opacity 0..1.</param>
/// <param name="NightDim">How far the face and bodies fade toward black at night, 0..1.</param>
/// <param name="Grain">Surface grain strength 0..1.</param>
public sealed record DialTheme(
    string Id,
    Rgba FaceCenter,
    Rgba FaceEdge,
    Rgba Ink,
    Rgba Hand,
    Rgba Accent,
    float ShadowStrength = 0.38f,
    float NightDim = 0.86f,
    float Grain = 0.35f)
{
    /// <summary>Localisation key for the display name.</summary>
    public string NameKey => "dial." + Id;
}

public static class DialCatalog
{
    private static DialTheme D(string id, string center, string edge, string index, string hand, string accent,
        float shadow = 0.38f, float dim = 0.86f, float grain = 0.35f) =>
        new(id, Rgba.FromHex(center), Rgba.FromHex(edge), Rgba.FromHex(index), Rgba.FromHex(hand), Rgba.FromHex(accent),
            shadow, dim, grain);

    public static IReadOnlyList<DialTheme> All { get; } = new[]
    {
        // face centre -> edge (very close: the dial fills the screen), ink, hand/pill, centre-cap accent
        D("white",      "#E9E6DF", "#DDD9D0", "#2B2A28", "#FBFAF7", "#8C8579", shadow: 0.30f),
        D("turquoise",  "#AEE9E3", "#93DAD3", "#0D5560", "#F2FFFD", "#0D5560", shadow: 0.28f, dim: 0.90f),
        D("glacier",    "#C4D8E1", "#B1C9D4", "#1D4F63", "#F4FAFC", "#1D4F63", shadow: 0.28f),
        D("ocean",      "#2C6E9C", "#1D5280", "#E8F3FA", "#F5FAFD", "#E8F3FA", shadow: 0.40f, dim: 0.82f),
        D("tennis",     "#DAEB6E", "#C5DA48", "#2C3A12", "#FBFFEA", "#2C3A12", shadow: 0.28f, dim: 0.90f),
        D("signal-blue","#3862E8", "#2548C2", "#F4F7FF", "#FFFFFF", "#F4F7FF", shadow: 0.42f, dim: 0.84f),
        D("salmon",     "#F5B6A8", "#EB9888", "#5A2418", "#FFF8F5", "#5A2418", shadow: 0.28f, dim: 0.90f),
        D("yellow",     "#FFE26E", "#F7CE42", "#3D2E00", "#FFFDF2", "#3D2E00", shadow: 0.26f, dim: 0.92f),
        D("beige",      "#E9DCC5", "#D9C9AC", "#3F3225", "#FFFCF6", "#3F3225", shadow: 0.30f),
        D("pistachio",  "#C7DEB3", "#B3CF9C", "#26401C", "#F9FFF4", "#26401C", shadow: 0.28f),
        D("lavender",   "#D0C7F1", "#BBAFE7", "#302760", "#FAF8FF", "#302760", shadow: 0.30f),
        D("rose",       "#F7CBD6", "#ECB2C0", "#5A2236", "#FFF8FA", "#5A2236", shadow: 0.28f),
        D("sky-blue",   "#A7D7F4", "#8EC8EB", "#12405E", "#F5FBFF", "#12405E", shadow: 0.28f),
        D("cream",      "#F7EDD5", "#ECDEBB", "#35302A", "#FFFEF9", "#35302A", shadow: 0.28f),
        D("slate",      "#5E6973", "#4B555E", "#EEF2F5", "#F7F9FA", "#EEF2F5", dim: 0.72f, shadow: 0.50f),
        D("noir",       "#222225", "#161618", "#E6E6E8", "#F4F4F5", "#E6E6E8", dim: 0.35f, shadow: 0.60f, grain: 0.28f),
    };

    public static DialTheme Default => All[0];

    public static DialTheme ById(string? id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}

