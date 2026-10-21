namespace Klock.Core.Themes;

public sealed record LumeColor(string Id, Rgba Color)
{
    public string NameKey => "lume." + Id;
}

public static class LumeCatalog
{
    private static LumeColor L(string id, string hex) => new(id, Rgba.FromHex(hex));

    public static IReadOnlyList<LumeColor> All { get; } = new[]
    {
        L("red",      "#FF3B30"),
        L("amber",    "#FFB000"),
        L("lavender", "#B79CFF"),
        L("rose",     "#FF7AA8"),
        L("seafoam",  "#6EE7B7"),
        L("sky",      "#5CC8FF"),
        L("bgw9",     "#A8F5EA"),
        L("peach",    "#FFB38A"),
        L("tritium",  "#9CFF6B"),
        L("aqua",     "#2DE2E6"),
        L("yellow",   "#FFE94D"),
        L("lime",     "#B6FF2E"),
        L("ice-blue", "#BEE6FF"),
        L("white",    "#F4F7FF"),
    };

    public static LumeColor Default => All.First(l => l.Id == "bgw9");

    public static LumeColor ById(string? id) =>
        All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
