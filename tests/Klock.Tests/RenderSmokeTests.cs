using Klock.Core;
using Klock.Core.Burnin;
using Klock.Core.Themes;
using Klock.Core.Time;
using Klock.Renderer;

namespace Klock.Tests;

/// <summary>
/// Renders real frames on the WARP software rasteriser (so it runs on any machine, including CI) and
/// inspects pixels. This is what proves the hands are drawn at the angles the movement engine says.
/// The hands are pale on every dial, so hand tests use the dark Ocean dial where they stand out.
/// </summary>
public class RenderSmokeTests
{
    private const int Size = 600;
    private const double SizeFraction = 0.70;
    private const double R = Size * SizeFraction / 2; // dial radius in pixels
    private const double C = Size / 2.0;

    private static ClockFrame Frame(string dial, double tod, double night = 0, MovementType move = MovementType.Digital,
        double sizeFraction = SizeFraction, BurnInLevel burn = BurnInLevel.Off, double t = 0, bool fullBleed = false) => new()
    {
        Dial = DialCatalog.ById(dial),
        Lume = LumeCatalog.Default,
        Night = night,
        Hands = MovementEngine.Evaluate(move, tod),
        BurnIn = PixelShift.Evaluate(t, Size, burn),
        SizeFraction = sizeFraction,
        Background = Rgba.Black,
        MatchDialBackground = fullBleed,
    };

    private static byte[] Render(ClockFrame f) => OffscreenRenderer.RenderBgra(f, Size, Size, forceSoftware: true);

    private static (int R, int G, int B) Px(byte[] bgra, double x, double y)
    {
        int xi = (int)Math.Round(x), yi = (int)Math.Round(y), i = (yi * Size + xi) * 4;
        return (bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    private static double Lum((int R, int G, int B) c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    private static (double X, double Y) At(double angleDeg, double radiusFrac)
    {
        double a = angleDeg * Math.PI / 180;
        return (C + Math.Sin(a) * R * radiusFrac, C - Math.Cos(a) * R * radiusFrac);
    }

    /// <summary>Brightest pixel across the hand's width (+/-7 px), tolerant of anti-aliasing and sub-pixel placement.</summary>
    private static double LightestAcross(byte[] img, double angleDeg, double radiusFrac)
    {
        var (cx, cy) = At(angleDeg, radiusFrac);
        double a = angleDeg * Math.PI / 180, px = Math.Cos(a), py = Math.Sin(a); // perpendicular
        double max = 0;
        for (int k = -7; k <= 7; k++) max = Math.Max(max, Lum(Px(img, cx + px * k, cy + py * k)));
        return max;
    }

    /// <summary>Brightest pixel in a small box: for the hairline seconds hand.</summary>
    private static double LightestInBox(byte[] img, double angleDeg, double radiusFrac)
    {
        var (cx, cy) = At(angleDeg, radiusFrac);
        double max = 0;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++) max = Math.Max(max, Lum(Px(img, cx + dx, cy + dy)));
        return max;
    }

    // An empty patch of face (inside the numerals, away from every hand used below).
    private static (double X, double Y) EmptyFace => (C + R * 0.15, C + R * 0.20);

    [Fact]
    public void Software_rasteriser_renders_a_non_blank_frame()
    {
        var img = Render(Frame("white", 10 * 3600 + 8 * 60 + 35));
        Assert.True(Lum(Px(img, EmptyFace.X, EmptyFace.Y)) > 200, "face should be near-white");
        Assert.Equal((0, 0, 0), Px(img, 4, 4)); // solid background outside the dial is untouched
    }

    [Fact]
    public void Full_bleed_fills_the_whole_screen_with_the_dial_colour()
    {
        var img = Render(Frame("turquoise", 3600, fullBleed: true));
        var corner = Px(img, 3, 3);
        Assert.True(Lum(corner) > 150, $"corner should be the (light) dial colour, got {corner}");
        Assert.True(corner.G > corner.R, "turquoise corner should be green-blue tinted");
    }

    [Theory]
    [InlineData(3, 0)]   // hour hand to 3, minute hand to 12
    [InlineData(9, 15)]
    [InlineData(12, 45)]
    [InlineData(4, 40)]
    [InlineData(6, 15)]
    public void Hour_and_minute_hands_point_where_the_movement_engine_says(int h, int m)
    {
        double tod = h * 3600 + m * 60;
        var img = Render(Frame("ocean", tod));
        var a = MovementEngine.Evaluate(MovementType.Digital, tod);

        double onMinute = LightestAcross(img, a.MinuteDeg, 0.40);
        double onHour = LightestAcross(img, a.HourDeg, 0.30);
        Assert.True(onMinute > 200, $"minute hand not found at {a.MinuteDeg:F1} deg (brightest {onMinute:F0})");
        Assert.True(onHour > 200, $"hour hand not found at {a.HourDeg:F1} deg (brightest {onHour:F0})");

        // ...and a clearly different direction is bare dial (the ocean face is mid-dark).
        double off = LightestAcross(img, a.MinuteDeg + 135, 0.30);
        Assert.True(off < 160, $"unexpected bright pixels off-axis ({off:F0})");
    }

    [Fact]
    public void Seconds_hand_sits_on_the_exact_second()
    {
        foreach (int sec in new[] { 15, 30, 45 })
        {
            var img = Render(Frame("ocean", 3600 + sec));
            double onHand = LightestInBox(img, sec * 6.0, 0.45);
            // 60 degrees further round: bare dial at 1:00 (hour hand at 30 deg, minute hand at 0-3 deg).
            double other = LightestInBox(img, (sec * 6.0 + 60) % 360, 0.45);
            Assert.True(onHand > 180, $"no seconds hand at {sec * 6} deg (brightest {onHand:F0})");
            Assert.True(other < 150, $"seconds hand also appears at {(sec * 6.0 + 60) % 360} deg ({other:F0})");
        }
    }

    [Fact]
    public void Quartz_second_hand_is_on_the_tick_mark_half_a_second_after_the_tick()
    {
        double tod = 3600 + 17.5; // 17 s + 0.5 s: spring fully settled
        var img = Render(Frame("ocean", tod, move: MovementType.Quartz));
        Assert.True(LightestInBox(img, 17 * 6.0, 0.45) > 180);
    }

    [Fact]
    public void Night_theme_darkens_the_dial_and_lights_the_pills()
    {
        var day = Render(Frame("ocean", 10 * 3600 + 10 * 60, night: 0));
        var night = Render(Frame("ocean", 10 * 3600 + 10 * 60, night: 1));
        Assert.True(Lum(Px(night, EmptyFace.X, EmptyFace.Y)) < Lum(Px(day, EmptyFace.X, EmptyFace.Y)) * 0.5, "night face should be much darker");

        // The 3 o'clock pill's groove glows in the lume colour (BGW9: bright cyan-white).
        var (x, y) = At(90, 0.80);
        var pill = Px(night, x, y);
        Assert.True(Lum(pill) > 150, $"3 o'clock pill should glow, got {pill}");
    }

    [Fact]
    public void Theme_crossfade_is_monotonic_in_brightness()
    {
        double prev = double.MaxValue;
        foreach (double n in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var img = Render(Frame("white", 3600, night: n));
            double l = Lum(Px(img, EmptyFace.X, EmptyFace.Y));
            Assert.True(l <= prev + 1, $"night={n}: {l} > {prev}");
            prev = l;
        }
    }

    [Fact]
    public void Compact_is_smaller_than_classic()
    {
        var classic = Render(Frame("white", 3600, sizeFraction: ClockController.ClassicFraction));
        var compact = Render(Frame("white", 3600, sizeFraction: ClockController.CompactFraction));
        // The 3 o'clock pill sits at 80% of the classic radius: pale on classic, outside the compact dial entirely.
        var (x, y) = At(90, 0.80);
        Assert.True(Lum(Px(classic, x, y)) > 150);
        Assert.True(Lum(Px(compact, x, y)) < 40);
    }

    [Fact]
    public void Pixel_shift_moves_the_dial_and_off_does_not()
    {
        var still = Render(Frame("white", 3600));
        var shifted = Render(Frame("white", 3600, burn: BurnInLevel.Extended, t: 100));
        var s = PixelShift.Evaluate(100, Size, BurnInLevel.Extended);
        Assert.True(Math.Abs(s.X) + Math.Abs(s.Y) > 1, "test time should produce a visible shift");
        Assert.NotEqual(still, shifted);
    }

    [Fact]
    public void Every_dial_renders_day_and_night_with_visible_furniture()
    {
        foreach (var d in DialCatalog.All)
            foreach (double night in new[] { 0.0, 1.0 })
            {
                var img = Render(Frame(d.Id, 10 * 3600 + 9 * 60 + 36, night, fullBleed: true));
                double bg = Lum(Px(img, 3, 3));
                // Numerals, ticks, pills and hands must stand out from the field: count clearly different pixels.
                int distinct = 0;
                for (int i = 0; i < img.Length; i += 4)
                {
                    double l = 0.2126 * img[i + 2] + 0.7152 * img[i + 1] + 0.0722 * img[i];
                    if (Math.Abs(l - bg) > 45) distinct++;
                }
                Assert.True(distinct > 1500, $"{d.Id} night={night}: only {distinct} distinct pixels (blank dial?)");
            }
    }

    [Fact]
    public void Hour_numerals_are_printed_at_their_positions()
    {
        // On the white dial the numerals are near-black ink: a dark pixel must exist around each hour position.
        var img = Render(Frame("white", 3600 * 5 + 1800, fullBleed: true)); // hands parked low-right, away from 12/3/9
        foreach (int h in new[] { 12, 3, 9 })
        {
            var (cx, cy) = At(h * 30.0, 0.59);
            double min = 255;
            for (int dx = -14; dx <= 14; dx++)
                for (int dy = -14; dy <= 14; dy++) min = Math.Min(min, Lum(Px(img, cx + dx, cy + dy)));
            Assert.True(min < 90, $"no numeral ink near hour {h} (darkest {min:F0})");
        }
    }
}
