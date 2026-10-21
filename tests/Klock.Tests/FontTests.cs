using System.Reflection;
using System.Security.Cryptography;
using Klock.Core;
using Klock.Core.Burnin;
using Klock.Core.Settings;
using Klock.Core.Themes;
using Klock.Core.Time;
using Klock.Renderer;

namespace Klock.Tests;

public class FontTests
{
    private static string AssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "assets", "fonts"))) dir = dir.Parent;
        return dir == null ? throw new DirectoryNotFoundException("assets/fonts not found") : Path.Combine(dir.FullName, "assets", "fonts");
    }

    public static IEnumerable<object[]> Fonts() => new[]
    {
        new object[] { "Panchang-Extrabold.otf", "Panchang-FFL.txt" },
        new object[] { "ClashDisplay-Semibold.otf", "ClashDisplay-FFL.txt" },
        new object[] { "Satoshi-Black.otf", "Satoshi-FFL.txt" },
        new object[] { "Melodrama-Bold.otf", "Melodrama-FFL.txt" },
    };

    [Theory]
    [MemberData(nameof(Fonts))]
    public void Embedded_font_is_byte_identical_to_the_supplied_file_and_ships_with_its_licence(string otf, string licence)
    {
        // The ITF Free Font License forbids modifying, subsetting or converting the fonts: only the original bytes may ship.
        var asm = typeof(FontLibrary).Assembly;
        using var s = asm.GetManifestResourceStream("Klock.Fonts." + otf);
        Assert.NotNull(s);
        using var ms = new MemoryStream();
        s!.CopyTo(ms);

        var onDisk = File.ReadAllBytes(Path.Combine(AssetsDir(), otf));
        Assert.Equal(SHA256.HashData(onDisk), SHA256.HashData(ms.ToArray()));

        var text = File.ReadAllText(Path.Combine(AssetsDir(), licence));
        Assert.Contains("ITF Free Font License", text);
    }

    [Fact]
    public void DirectWrite_loads_all_three_fonts_from_memory()
    {
        var lib = FontLibrary.Instance;
        Assert.NotNull(lib);
        Assert.True(lib!.Has(NumeralFont.Panchang));
        Assert.True(lib.Has(NumeralFont.ClashDisplay));
        Assert.True(lib.Has(NumeralFont.Satoshi));
        Assert.True(lib.Has(NumeralFont.Melodrama));
        Assert.False(lib.Has(NumeralFont.Klock)); // built-in face: not a DirectWrite font
    }

    [Fact]
    public void Numeral_run_is_centred_and_sized_by_the_ink_of_the_digits()
    {
        var lib = FontLibrary.Instance!;
        foreach (var f in new[] { NumeralFont.Panchang, NumeralFont.ClashDisplay, NumeralFont.Satoshi, NumeralFont.Melodrama })
        {
            using var run = lib.CreateRun(f, "12", 100f);
            Assert.True(run.Layout.Metrics.Width > 50, $"{f}: two digits at 100 px should be wider than 50 px");
            Assert.True(run.Origin.X < -25, $"{f}: layout should extend left of the centre");
            Assert.True(run.Origin.Y < -30 && run.Origin.Y > -400, $"{f}: baseline offset implausible ({run.Origin.Y})");
        }
    }

    // ---- rendering with each face ----

    private const int Size = 600;
    private const double R = Size * 0.70 / 2, C = Size / 2.0;

    private static byte[] Render(NumeralFont font) => OffscreenRenderer.RenderBgra(new ClockFrame
    {
        Dial = DialCatalog.ById("white"),
        Lume = LumeCatalog.Default,
        Night = 0,
        Hands = MovementEngine.Evaluate(MovementType.Digital, 5 * 3600 + 1800), // parked low-right, away from 12/3/9
        BurnIn = PixelShift.Evaluate(0, Size, BurnInLevel.Off),
        SizeFraction = 0.70,
        Background = Rgba.Black,
        MatchDialBackground = true,
        Numerals = font,
    }, Size, Size, forceSoftware: true);

    private static double DarkestNear(byte[] img, double angleDeg, double radiusFrac)
    {
        double a = angleDeg * Math.PI / 180, cx = C + Math.Sin(a) * R * radiusFrac, cy = C - Math.Cos(a) * R * radiusFrac;
        double min = 255;
        for (int dx = -16; dx <= 16; dx++)
            for (int dy = -16; dy <= 16; dy++)
            {
                int i = ((int)Math.Round(cy + dy) * Size + (int)Math.Round(cx + dx)) * 4;
                min = Math.Min(min, 0.2126 * img[i + 2] + 0.7152 * img[i + 1] + 0.0722 * img[i]);
            }
        return min;
    }

    [Theory]
    [InlineData(NumeralFont.Panchang)]
    [InlineData(NumeralFont.ClashDisplay)]
    [InlineData(NumeralFont.Satoshi)]
    [InlineData(NumeralFont.Melodrama)]
    [InlineData(NumeralFont.Klock)]
    public void Every_numeral_face_prints_ink_at_the_hour_positions(NumeralFont font)
    {
        var img = Render(font);
        foreach (int h in new[] { 12, 3, 9, 6 })
            Assert.True(DarkestNear(img, h * 30.0, 0.59) < 100, $"{font}: no numeral ink near hour {h}");
    }

    [Fact]
    public void The_faces_really_are_different()
    {
        var p = Render(NumeralFont.Panchang);
        Assert.NotEqual(p, Render(NumeralFont.ClashDisplay));
        Assert.NotEqual(p, Render(NumeralFont.Satoshi));
        Assert.NotEqual(p, Render(NumeralFont.Melodrama));
        Assert.NotEqual(p, Render(NumeralFont.Klock));
    }

    [Fact]
    public void Numerals_setting_defaults_to_panchang_and_repairs_garbage()
    {
        Assert.Equal(NumeralFont.Panchang, new KlockSettings().Numerals);
        Assert.Equal(NumeralFont.Panchang, SettingsJson.Deserialize("""{ "Numerals": 42 }""").Numerals);
        Assert.Equal(NumeralFont.Melodrama, SettingsJson.Deserialize("""{ "Numerals": "Melodrama" }""").Numerals);
    }

    [Fact]
    public void A_retired_font_name_falls_back_without_discarding_the_other_settings()
    {
        // "Boxing" shipped in an earlier build. Loading such a file must keep dial, movement and language.
        var s = SettingsJson.Deserialize("""{ "DialId": "ocean", "Movement": "Mechanical", "Language": "fr", "Numerals": "Boxing" }""");
        Assert.Equal(NumeralFont.Panchang, s.Numerals);
        Assert.Equal("ocean", s.DialId);
        Assert.Equal(Klock.Core.Time.MovementType.Mechanical, s.Movement);
        Assert.Equal("fr", s.Language);
    }
}

