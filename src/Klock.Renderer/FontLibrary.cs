using System.Reflection;
using System.Runtime.InteropServices;
using Klock.Core;
using Klock.Core.Themes;
using Vortice.DirectWrite;

namespace Klock.Renderer;

/// <summary>
/// The numeral typefaces, loaded straight from the unmodified .otf files embedded in this assembly through
/// DirectWrite's in-memory loader. Nothing is written to disk, subsetted or converted (the fonts' licence forbids
/// that); the fonts are only ever used to draw digits. If a font is missing or DirectWrite is too old, that
/// font simply reports <see cref="Has"/> = false and the painter uses the built-in digit face.
/// </summary>
public sealed class FontLibrary : IDisposable
{
    private sealed record Entry(IDWriteFontCollection1 Collection, string Family, FontWeight Weight, FontStyle Style,
        FontStretch Stretch, float DigitHeightEm, float DigitCenterEm);

    private static readonly Lazy<FontLibrary?> Shared = new(Create);

    /// <summary>Process-wide library, or null when DirectWrite font sets are unavailable.</summary>
    public static FontLibrary? Instance => Shared.Value;

    private readonly IDWriteFactory5 _factory;
    private readonly IDWriteInMemoryFontFileLoader _loader;
    private readonly Dictionary<NumeralFont, Entry> _entries = new();
    private readonly List<GCHandle> _pins = new(); // in-memory font data must stay put while DirectWrite uses it

    private static readonly (NumeralFont Font, string Resource)[] Sources =
    {
        (NumeralFont.Panchang, "Klock.Fonts.Panchang-Extrabold.otf"),
        (NumeralFont.ClashDisplay, "Klock.Fonts.ClashDisplay-Semibold.otf"),
        (NumeralFont.Satoshi, "Klock.Fonts.Satoshi-Black.otf"),
        (NumeralFont.Melodrama, "Klock.Fonts.Melodrama-Bold.otf"),
    };

    private FontLibrary(IDWriteFactory5 factory, IDWriteInMemoryFontFileLoader loader)
    {
        _factory = factory;
        _loader = loader;
    }

    private static FontLibrary? Create()
    {
        try
        {
            var factory = DWrite.DWriteCreateFactory<IDWriteFactory5>();
            var loader = factory.CreateInMemoryFontFileLoader();
            factory.RegisterFontFileLoader(loader);
            var lib = new FontLibrary(factory, loader);
            foreach (var (font, resource) in Sources) lib.TryLoad(font, resource);
            Logger.Info($"Numeral fonts loaded: {string.Join(", ", lib._entries.Keys)}");
            return lib;
        }
        catch (Exception ex)
        {
            // DirectWrite font sets need Windows 10 1703+; the built-in digits still work without them.
            Logger.Warn($"Numeral fonts unavailable, using the built-in face: {ex.Message}");
            return null;
        }
    }

    private void TryLoad(NumeralFont font, string resource)
    {
        try
        {
            using var s = typeof(FontLibrary).Assembly.GetManifestResourceStream(resource);
            if (s == null) { Logger.Warn($"Font resource {resource} is not embedded"); return; }

            var bytes = new byte[s.Length];
            s.ReadExactly(bytes);
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            _pins.Add(pin);

            using var file = _loader.CreateInMemoryFontFileReference(_factory, pin.AddrOfPinnedObject(), (uint)bytes.Length, null);
            using var builder = _factory.CreateFontSetBuilder();
            builder.AddFontFile(file);
            using var set = builder.CreateFontSet();
            var collection = _factory.CreateFontCollectionFromFontSet(set);

            using var family = collection.GetFontFamily(0);
            using var names = family.FamilyNames;
            string familyName = names.GetString(0);
            using var f = family.GetFont(0);
            using var face = f.CreateFontFace();

            // Measure the real ink of the digit "0" so any typeface is centred and sized by what you see.
            var glyphs = face.GetGlyphIndices(new uint[] { '0' });
            var gm = face.GetDesignGlyphMetrics(glyphs, false)[0];
            float upm = face.Metrics.DesignUnitsPerEm;
            float inkHeight = gm.AdvanceHeight - gm.TopSideBearing - gm.BottomSideBearing;
            float inkTop = gm.VerticalOriginY - gm.TopSideBearing;           // above the baseline
            float centre = inkTop - inkHeight / 2f;

            _entries[font] = new Entry(collection, familyName, f.Weight, f.Style, f.Stretch, inkHeight / upm, centre / upm);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not load {resource}: {ex.Message}");
        }
    }

    public bool Has(NumeralFont font) => _entries.ContainsKey(font);

    /// <summary>A laid-out run of digits sized so the digit ink is <paramref name="digitHeightPx"/> tall.</summary>
    public sealed class Run : IDisposable
    {
        public required IDWriteTextLayout Layout { get; init; }
        /// <summary>Top-left of the layout relative to the point the digits should be centred on.</summary>
        public required System.Numerics.Vector2 Origin { get; init; }
        public void Dispose() => Layout.Dispose();
    }

    public Run CreateRun(NumeralFont font, string text, float digitHeightPx)
    {
        var e = _entries[font];
        float size = digitHeightPx / e.DigitHeightEm;

        using var format = _factory.CreateTextFormat(e.Family, e.Collection, e.Weight, e.Style, e.Stretch, size, "en-us");
        format.WordWrapping = WordWrapping.NoWrap;
        var layout = _factory.CreateTextLayout(text, format, size * text.Length * 2f + 8f, size * 2f);

        var lines = new LineMetrics[1];
        layout.GetLineMetrics(lines, out _);
        float width = layout.Metrics.Width;
        float baseline = lines[0].Baseline;
        // The ink's centre sits e.DigitCenterEm * size above the baseline.
        float originY = -(baseline - e.DigitCenterEm * size);
        return new Run { Layout = layout, Origin = new System.Numerics.Vector2(-width / 2f, originY) };
    }

    public void Dispose()
    {
        foreach (var e in _entries.Values) e.Collection.Dispose();
        _factory.UnregisterFontFileLoader(_loader);
        _loader.Dispose();
        _factory.Dispose();
        foreach (var p in _pins) p.Free();
    }
}

