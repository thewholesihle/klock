using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Klock.Renderer;

/// <summary>
/// An original extended, monoline digit face drawn as vector skeletons (no fonts, so it renders identically
/// everywhere). Each digit lives in a box 0..Width wide and 0..1 tall and is stroked with <see cref="Stroke"/>;
/// the visible cap height is therefore 1 + Stroke.
/// </summary>
public sealed class Glyphs : IDisposable
{
    public const float Stroke = 0.25f;  // weight matched to the display faces beside it
    public const float Gap = 0.20f;
    private const float W = 0.84f;      // skeleton width of a wide digit
    private const float W1 = 0.40f;     // skeleton width of "1"

    private readonly ID2D1Factory1 _factory;
    private readonly Dictionary<char, ID2D1Geometry> _geo = new();
    public ID2D1StrokeStyle StrokeStyle { get; }

    public Glyphs(ID2D1Factory1 factory)
    {
        _factory = factory;
        StrokeStyle = factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Flat,
            EndCap = CapStyle.Flat,
            DashCap = CapStyle.Flat,
            LineJoin = LineJoin.Miter,
            MiterLimit = 3f,
            DashStyle = DashStyle.Solid,
        });
        foreach (char c in "0123456789") _geo[c] = Build(c);
    }

    public ID2D1Geometry Get(char c) => _geo[c];

    public static float SkeletonWidth(char c) => c == '1' ? W1 : W;

    /// <summary>Visible width of the string, in units where the visible cap height is 1 + <see cref="Stroke"/>.</summary>
    public static float Measure(string s)
    {
        float w = 0;
        for (int i = 0; i < s.Length; i++) w += SkeletonWidth(s[i]) + Stroke + (i > 0 ? Gap : 0);
        return w;
    }

    /// <summary>Left edge (skeleton origin) of each glyph relative to the centre of the string.</summary>
    public static IEnumerable<(char Ch, float X)> Layout(string s)
    {
        float x = -Measure(s) / 2f;
        foreach (char c in s)
        {
            yield return (c, x + Stroke / 2f);
            x += SkeletonWidth(c) + Stroke + Gap;
        }
    }

    // ------------------------------------------------------------------ skeletons

    private ID2D1Geometry Build(char c)
    {
        if (c == '9')
        {
            using var six = Build('6');
            return _factory.CreateTransformedGeometry(six, Matrix3x2.CreateRotation(MathF.PI, new Vector2(W / 2f, 0.5f)));
        }

        var path = _factory.CreatePathGeometry();
        using var s = path.Open();
        var p = new Pen(s);
        switch (c)
        {
            case '0':
                p.Begin(0.30f, 0).Line(W - 0.30f, 0).Arc(W - 0.30f, 1, 0.30f, 0.5f, cw: true).Line(0.30f, 1).Arc(0.30f, 0, 0.30f, 0.5f, cw: true).End(closed: true);
                break;
            case '1':
                p.Begin(W1, 0).Line(W1, 1).End(false);
                p.Begin(W1, 0).Line(0, 0.20f).End(false);
                break;
            case '2':
                p.Begin(0.02f, 0.30f).Arc(W - 0.02f, 0.30f, 0.41f, 0.30f, cw: true).Line(0f, 1f).Line(W, 1f).End(false);
                break;
            case '3':
                p.Begin(0f, 0f).Line(W - 0.27f, 0f).Arc(W - 0.27f, 0.5f, 0.27f, 0.25f, cw: true).Line(0.22f, 0.5f).End(false);
                p.Begin(W - 0.27f, 0.5f).Arc(W - 0.27f, 1f, 0.27f, 0.25f, cw: true).Line(0f, 1f).End(false);
                break;
            case '4':
                p.Begin(W - 0.20f, 1f).Line(W - 0.20f, 0f).Line(0f, 0.70f).Line(W, 0.70f).End(false);
                break;
            case '5':
                p.Begin(W, 0f).Line(0.06f, 0f).Line(0.06f, 0.46f).Line(W - 0.28f, 0.46f).Arc(W - 0.28f, 1f, 0.28f, 0.27f, cw: true).Line(0f, 1f).End(false);
                break;
            case '6':
                p.Begin(W, 0f).Line(0.30f, 0f).Arc(0f, 0.30f, 0.30f, 0.30f, cw: false).Line(0f, 0.70f).End(false);
                p.Begin(0.30f, 0.40f).Line(W - 0.30f, 0.40f).Arc(W, 0.70f, 0.30f, 0.30f, cw: true).Arc(W - 0.30f, 1f, 0.30f, 0.30f, cw: true)
                    .Line(0.30f, 1f).Arc(0f, 0.70f, 0.30f, 0.30f, cw: true).Arc(0.30f, 0.40f, 0.30f, 0.30f, cw: true).End(closed: true);
                break;
            case '7':
                p.Begin(0f, 0f).Line(W, 0f).Line(0.20f, 1f).End(false);
                break;
            case '8':
                p.Begin(0.29f, 0f).Line(W - 0.29f, 0f).Arc(W - 0.29f, 0.5f, 0.25f, 0.25f, cw: true).Line(0.29f, 0.5f).Arc(0.29f, 0f, 0.25f, 0.25f, cw: true).End(closed: true);
                p.Begin(0.25f, 0.5f).Line(W - 0.25f, 0.5f).Arc(W - 0.25f, 1f, 0.25f, 0.25f, cw: true).Line(0.25f, 1f).Arc(0.25f, 0.5f, 0.25f, 0.25f, cw: true).End(closed: true);
                break;
        }
        s.Close();
        return path;
    }

    /// <summary>Tiny fluent helper over a geometry sink.</summary>
    private sealed class Pen(ID2D1GeometrySink sink)
    {
        private bool _open;

        public Pen Begin(float x, float y)
        {
            sink.BeginFigure(new Vector2(x, y), FigureBegin.Hollow);
            _open = true;
            return this;
        }

        public Pen Line(float x, float y) { sink.AddLine(new Vector2(x, y)); return this; }

        public Pen Arc(float x, float y, float rx, float ry, bool cw)
        {
            sink.AddArc(new ArcSegment
            {
                Point = new Vector2(x, y),
                Size = new Size(rx, ry),
                RotationAngle = 0,
                SweepDirection = cw ? SweepDirection.Clockwise : SweepDirection.CounterClockwise,
                ArcSize = ArcSize.Small,
            });
            return this;
        }

        public void End(bool closed)
        {
            if (_open) sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
            _open = false;
        }
    }

    public void Dispose()
    {
        foreach (var g in _geo.Values) g.Dispose();
        StrokeStyle.Dispose();
    }
}

