using System.Numerics;
using System.Runtime.InteropServices;
using Klock.Core;
using Klock.Core.Themes;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using AlphaMode = Vortice.DCommon.AlphaMode;

namespace Klock.Renderer;

/// <summary>
/// Draws the wordless-but-numbered dial: a full-bleed tonal face with fine grain, large extended hour numerals,
/// a minute ring of dashes with small tangential numerals, embossed pill indices, and slim pointed hands with a
/// recessed channel. Static art is rendered once into cached layers at "master" size; each frame only the hands,
/// their soft shadows and (at night) the lume bloom are drawn live. Everything is procedural and resolution independent.
/// </summary>
public sealed class ClockPainter : IDisposable
{
    // ---- Layout, in units of the dial radius R (the outer edge of the minute numerals is ~1.0R).
    private const float PillCenter = 0.800f, PillLength = 0.180f, PillWidth = 0.064f, QuarterScale = 1.10f;
    private const float TickCenter = 0.852f, TickLength = 0.056f, TickWidth = 0.0105f;
    private const float HourNumRadius = 0.590f, HourNumHeight = 0.126f;
    private const float MinNumRadius = 0.972f, MinNumHeight = 0.056f;
    /// <summary>Solid-background mode: radius of the dial disc behind the art.</summary>
    private const float DiscRadius = 1.075f;

    private readonly ID2D1DeviceContext _dc;
    private readonly RenderDevice _dev;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly Shadow _shadowFx;
    private readonly GaussianBlur _blurTight, _blurWide;
    private readonly Glyphs _glyphs;
    private ID2D1Bitmap1? _grain;
    private ID2D1BitmapBrush1? _grainBrush;

    // static layers
    private ID2D1Bitmap1? _day, _night, _spec;
    private string _cacheKey = "";
    private NumeralFont _numerals = NumeralFont.Panchang;
    private int _layer;
    private float _masterR;

    // screen-space dial gradients (full-bleed background / disc)
    private ID2D1RadialGradientBrush? _bgDay, _bgNight;
    private string _bgKey = "";

    // hand geometry for the current radius
    private HandGeo? _hands;
    private float _handsR = -1;

    private static readonly PixelFormat Pf = new(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);

    public ClockPainter(RenderDevice dev, ID2D1DeviceContext dc)
    {
        _dev = dev;
        _dc = dc;
        _brush = dc.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
        _shadowFx = new Shadow(dc);
        _blurTight = new GaussianBlur(dc);
        _blurWide = new GaussianBlur(dc);
        _glyphs = new Glyphs(dev.D2DFactory);
        dc.TextAntialiasMode = TextAntialiasMode.Grayscale; // text is drawn into transparent layers
        CreateGrain();
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Paint one frame into <paramref name="target"/> (width x height pixels).</summary>
    public void Paint(ID2D1Bitmap1 target, int width, int height, ClockFrame f)
    {
        float shortEdge = Math.Min(width, height);
        float rf = (float)(f.SizeFraction * shortEdge / 2.0);
        float cx = width / 2f + (float)(f.BurnIn.X + f.OffsetX * shortEdge);
        float cy = height / 2f + (float)(f.BurnIn.Y + f.OffsetY * shortEdge);
        float night = (float)f.Night;
        float lumeCap = (float)f.BurnIn.LumeCap;
        float hl = (float)f.BurnIn.HighlightScale;
        var dial = f.Dial;

        EnsureCache(dial, f.Lume, lumeCap, shortEdge, width, height, f.Numerals);

        // ---- Colours for this frame
        var faceMid = Rgba.Lerp(dial.FaceCenter, dial.FaceEdge, 0.5f);
        var nightFace = Rgba.Lerp(faceMid, Rgba.Black, dial.NightDim);
        var lumeLive = f.Lume.Color.Scale(lumeCap);
        var handFill = Rgba.Lerp(dial.Hand, nightFace.Scale(0.92f), night);
        var handEdge = Rgba.Lerp(dial.Ink, lumeLive, night);
        float handEdgeAlpha = 0.22f + 0.0f * night;
        var channelDay = Rgba.Lerp(dial.Hand, faceMid, 0.50f);
        var channel = Rgba.Lerp(channelDay, lumeLive, night);
        var secondsFill = Rgba.Lerp(dial.Hand, lumeLive.Scale(0.62f), night);
        var capFace = Rgba.Lerp(dial.Hand, nightFace.Scale(1.4f), night);
        var capDot = Rgba.Lerp(dial.Accent, lumeLive, night);

        // ---- Hand geometry (pixels, cached per radius)
        var hand = GetHands(rf);
        var hourM = Rotation(f.Hands.HourDeg, cx, cy);
        var minM = Rotation(f.Hands.MinuteDeg, cx, cy);
        var secM = Rotation(f.Hands.SecondDeg, cx, cy);

        // ---- Pre-record command lists (cannot be recorded inside BeginDraw)
        float shadowAlpha = dial.ShadowStrength * (1f - 0.55f * night);
        using var shadowHm = Record(() =>
        {
            _dc.Transform = hourM; Fill(hand.Hour, Rgba.Black);
            _dc.Transform = minM; Fill(hand.Minute, Rgba.Black);
        });
        using var shadowSec = Record(() =>
        {
            _dc.Transform = secM;
            Fill(hand.Second, Rgba.Black);
            Disc(0, hand.SecondTailY, hand.SecondTailR, Rgba.Black);
        });
        ID2D1CommandList? bloom = null;
        if (night > 0.01f)
        {
            bloom = Record(() =>
            {
                _dc.Transform = hourM; Fill(hand.HourChannel, lumeLive);
                _dc.Transform = minM; Fill(hand.MinuteChannel, lumeLive);
            });
        }

        // ---- Draw
        _dc.Target = target;
        _dc.BeginDraw();
        _dc.Transform = Matrix3x2.Identity;

        if (f.MatchDialBackground)
        {
            _dc.Clear(ToColor(Rgba.Lerp(dial.FaceEdge, nightFace, night)));
            var full = new RawRectF(0, 0, width, height);
            _dc.FillRectangle(full, _bgDay!);
            if (night > 0.001f) { _bgNight!.Opacity = night; _dc.FillRectangle(full, _bgNight); }
            DrawGrain(full, dial.Grain, night);
        }
        else
        {
            _dc.Clear(ToColor(f.Background));
            // A dial disc so dark ink and white hands always have their face behind them.
            var disc = new Ellipse(new Vector2(cx, cy), rf * DiscRadius, rf * DiscRadius);
            _dc.FillEllipse(disc, _bgDay!);
            if (night > 0.001f) { _bgNight!.Opacity = night; _dc.FillEllipse(disc, _bgNight); }
            DrawGrainDisc(disc, dial.Grain, night);
            Ring(cx, cy, rf * DiscRadius - rf * 0.004f, rf * 0.008f, Rgba.Black, 0.18f);
        }

        // Static layers, scaled from master resolution to the live size.
        float s = rf / _masterR;
        _dc.Transform = Matrix3x2.CreateScale(s) * Matrix3x2.CreateTranslation(cx - s * _layer / 2f, cy - s * _layer / 2f);
        var dest = new RawRectF(0, 0, _layer, _layer);
        _dc.DrawBitmap(_day!, dest, 1f, InterpolationMode.HighQualityCubic, null, null);
        if (night > 0.001f) _dc.DrawBitmap(_night!, dest, night, InterpolationMode.HighQualityCubic, null, null);
        float specOpacity = Math.Clamp(hl * (1f - 0.7f * night), 0f, 1f);
        _dc.DrawBitmap(_spec!, dest, specOpacity, InterpolationMode.HighQualityCubic, null, null);
        _dc.Transform = Matrix3x2.Identity;

        // Hour + minute hands: shadow, body, channel
        DrawShadow(shadowHm, rf * 0.011f, new Vector2(rf * 0.016f, rf * 0.030f), shadowAlpha);
        DrawHand(hourM, hand.Hour, handFill, hand.HourChannel, channel, handEdge, handEdgeAlpha, rf, night);
        DrawHand(minM, hand.Minute, handFill, hand.MinuteChannel, channel, handEdge, handEdgeAlpha, rf, night);

        if (bloom != null)
        {
            DrawBloom(bloom, rf, night * 0.85f);
            bloom.Dispose();
        }

        // Seconds hand rides highest: longer shadow, drawn last
        DrawShadow(shadowSec, rf * 0.014f, new Vector2(rf * 0.026f, rf * 0.052f), shadowAlpha * 0.9f);
        _dc.Transform = secM;
        Fill(hand.Second, secondsFill);
        Stroke(hand.Second, handEdge, handEdgeAlpha * 0.8f, rf * 0.0030f);
        Disc(0, hand.SecondTailY, hand.SecondTailR, secondsFill);
        _dc.Transform = Matrix3x2.Identity;

        // Layered centre cap: shadow ring, bevelled metal, darker inner, accent dot
        _dc.Transform = Matrix3x2.CreateTranslation(cx, cy);
        Disc(rf * 0.006f, rf * 0.012f, rf * 0.050f, Rgba.Black, 0.18f * (1f - 0.5f * night));
        Disc(0, 0, rf * 0.046f, capFace);
        Ring(0, 0, rf * 0.046f, rf * 0.0035f, handEdge, 0.28f);
        Disc(0, 0, rf * 0.034f, Rgba.Lerp(capFace, dial.Ink, 0.12f));
        Disc(0, 0, rf * 0.026f, capFace);
        Disc(-rf * 0.006f, -rf * 0.007f, rf * 0.012f, Rgba.White, 0.35f * (1f - night));
        Disc(0, 0, rf * 0.011f, capDot);
        _dc.Transform = Matrix3x2.Identity;

        _dc.EndDraw();
        _dc.Target = null;
    }

    public void Dispose()
    {
        _day?.Dispose(); _night?.Dispose(); _spec?.Dispose();
        _bgDay?.Dispose(); _bgNight?.Dispose();
        _hands?.Dispose();
        _grainBrush?.Dispose(); _grain?.Dispose();
        _glyphs.Dispose();
        _shadowFx.Dispose(); _blurTight.Dispose(); _blurWide.Dispose();
        _brush.Dispose();
    }

    // ------------------------------------------------------------------ hands

    private sealed class HandGeo : IDisposable
    {
        public required ID2D1Geometry Hour, HourChannel, Minute, MinuteChannel, Second;
        public float SecondTailY, SecondTailR;

        public void Dispose()
        {
            Hour.Dispose(); HourChannel.Dispose(); Minute.Dispose(); MinuteChannel.Dispose(); Second.Dispose();
        }
    }

    private HandGeo GetHands(float rf)
    {
        if (_hands != null && Math.Abs(_handsR - rf) < 0.05f) return _hands;
        _hands?.Dispose();
        _handsR = rf;
        _hands = new HandGeo
        {
            // tip length, tail length, half width, taper length (all x R)
            Hour = Tapered(rf, 0.560f, 0.130f, 0.027f, 0.085f),
            HourChannel = Slot(rf, 0.500f, 0.030f, 0.0115f),
            Minute = Tapered(rf, 0.865f, 0.150f, 0.0215f, 0.105f),
            MinuteChannel = Slot(rf, 0.790f, 0.035f, 0.0088f),
            Second = Needle(rf, 0.905f, 0.150f, 0.0042f),
            SecondTailY = 0.150f * rf,
            SecondTailR = 0.0125f * rf,
        };
        return _hands;
    }

    /// <summary>A bar with a rounded tail and a pointed, tapered tip, pointing up (negative y) from the origin.</summary>
    private ID2D1Geometry Tapered(float r, float tip, float tail, float halfW, float taper)
    {
        var g = _dev.D2DFactory.CreatePathGeometry();
        using var s = g.Open();
        float w = halfW * r, t = tip * r, tl = tail * r, tp = taper * r;
        s.BeginFigure(new Vector2(-w, tl - w), FigureBegin.Filled);
        s.AddArc(new ArcSegment { Point = new Vector2(w, tl - w), Size = new Size(w, w), RotationAngle = 0, SweepDirection = SweepDirection.CounterClockwise, ArcSize = ArcSize.Small });
        s.AddLine(new Vector2(w, -t + tp));
        s.AddLine(new Vector2(0, -t));
        s.AddLine(new Vector2(-w, -t + tp));
        s.EndFigure(FigureEnd.Closed);
        s.Close();
        return g;
    }

    /// <summary>The recessed lume channel: pointed at the tip end, rounded at the tail end.</summary>
    private ID2D1Geometry Slot(float r, float tip, float tailY, float halfW)
    {
        var g = _dev.D2DFactory.CreatePathGeometry();
        using var s = g.Open();
        float w = halfW * r, t = tip * r, bottom = tailY * r, point = w * 3.6f;
        s.BeginFigure(new Vector2(0, -t), FigureBegin.Filled);
        s.AddLine(new Vector2(w, -t + point));
        s.AddLine(new Vector2(w, bottom - w));
        s.AddArc(new ArcSegment { Point = new Vector2(-w, bottom - w), Size = new Size(w, w), RotationAngle = 0, SweepDirection = SweepDirection.Clockwise, ArcSize = ArcSize.Small });
        s.AddLine(new Vector2(-w, -t + point));
        s.EndFigure(FigureEnd.Closed);
        s.Close();
        return g;
    }

    /// <summary>The slim seconds hand: a long needle to a point, with a short tail.</summary>
    private ID2D1Geometry Needle(float r, float tip, float tail, float halfW)
    {
        var g = _dev.D2DFactory.CreatePathGeometry();
        using var s = g.Open();
        float w = Math.Max(halfW * r, 0.6f), t = tip * r, tl = tail * r; // never thinner than ~1 px
        s.BeginFigure(new Vector2(0, -t), FigureBegin.Filled);
        s.AddLine(new Vector2(w, -t + w * 14f));
        s.AddLine(new Vector2(w * 1.5f, tl));
        s.AddLine(new Vector2(-w * 1.5f, tl));
        s.AddLine(new Vector2(-w, -t + w * 14f));
        s.EndFigure(FigureEnd.Closed);
        s.Close();
        return g;
    }

    private void DrawHand(Matrix3x2 m, ID2D1Geometry body, Rgba bodyColor, ID2D1Geometry channelShape, Rgba channelColor,
        Rgba edgeColor, float edgeAlpha, float rf, float night)
    {
        _dc.Transform = m;
        Fill(body, bodyColor);
        Stroke(body, edgeColor, edgeAlpha, rf * 0.0032f);

        // Recessed channel: inner lip in shadow, fill, then a thin light edge on the lower-right (bevel)
        Stroke(channelShape, Rgba.Black, 0.22f * (1f - 0.6f * night), rf * 0.0042f);
        Fill(channelShape, channelColor);
        _dc.Transform = Matrix3x2.CreateTranslation(rf * 0.0016f, rf * 0.0022f) * m;
        Stroke(channelShape, Rgba.White, 0.55f * (1f - night), rf * 0.0022f);
        _dc.Transform = Matrix3x2.Identity;
    }

    // ------------------------------------------------------------------ effects

    private void DrawShadow(ID2D1CommandList cl, float sigma, Vector2 offset, float alpha)
    {
        if (alpha <= 0.001f) return;
        _shadowFx.SetInput(0, cl, true);
        _shadowFx.BlurStandardDeviation = Math.Max(0.5f, sigma);
        _shadowFx.Color = new Vector4(0, 0, 0, Math.Clamp(alpha, 0, 1));
        _dc.Transform = Matrix3x2.CreateTranslation(offset);
        _dc.DrawImage(_shadowFx, InterpolationMode.Linear, CompositeMode.SourceOver);
        _dc.Transform = Matrix3x2.Identity;
    }

    private void DrawBloom(ID2D1CommandList cl, float rf, float strength)
    {
        _blurTight.SetInput(0, cl, true);
        _blurTight.StandardDeviation = Math.Max(1f, rf * 0.012f);
        _blurWide.SetInput(0, cl, true);
        _blurWide.StandardDeviation = Math.Max(2f, rf * 0.050f);
        using var op = new Opacity(_dc);
        op.SetInput(0, _blurTight.Output, true);
        op.Value = Math.Clamp(strength, 0, 1);
        _dc.DrawImage(op, InterpolationMode.Linear, CompositeMode.Plus);
        using var op2 = new Opacity(_dc);
        op2.SetInput(0, _blurWide.Output, true);
        op2.Value = Math.Clamp(strength * 0.7f, 0, 1);
        _dc.DrawImage(op2, InterpolationMode.Linear, CompositeMode.Plus);
    }

    // ------------------------------------------------------------------ background / grain

    private void DrawGrain(RawRectF area, float strength, float night)
    {
        if (_grainBrush == null) return;
        _grainBrush.Opacity = strength * 0.075f * (1f - 0.45f * night);
        _dc.FillRectangle(area, _grainBrush);
    }

    private void DrawGrainDisc(Ellipse disc, float strength, float night)
    {
        if (_grainBrush == null) return;
        _grainBrush.Opacity = strength * 0.075f * (1f - 0.45f * night);
        _dc.FillEllipse(disc, _grainBrush);
    }

    private void EnsureBackground(DialTheme d, int w, int h)
    {
        string key = $"{d.Id}|{w}x{h}";
        if (key == _bgKey && _bgDay != null) return;
        _bgDay?.Dispose(); _bgNight?.Dispose();
        _bgKey = key;

        var faceMid = Rgba.Lerp(d.FaceCenter, d.FaceEdge, 0.5f);
        var nc = Rgba.Lerp(d.FaceCenter, Rgba.Black, d.NightDim);
        var ne = Rgba.Lerp(d.FaceEdge, Rgba.Black, Math.Min(1f, d.NightDim + 0.05f));
        _bgDay = MakeBackground(d.FaceCenter, d.FaceEdge, w, h);
        _bgNight = MakeBackground(nc, ne, w, h);
    }

    private ID2D1RadialGradientBrush MakeBackground(Rgba center, Rgba edge, int w, int h)
    {
        using var stops = _dc.CreateGradientStopCollection(new[]
        {
            new GradientStop { Position = 0f, Color = ToColor(center) },
            new GradientStop { Position = 1f, Color = ToColor(edge) },
        });
        float radius = MathF.Max(w, h) * 0.78f;
        return _dc.CreateRadialGradientBrush(new RadialGradientBrushProperties
        {
            Center = new Vector2(w * 0.46f, h * 0.40f),
            GradientOriginOffset = Vector2.Zero,
            RadiusX = radius,
            RadiusY = radius,
        }, stops);
    }

    // ------------------------------------------------------------------ static layers

    private void EnsureCache(DialTheme dial, LumeColor lume, float lumeCap, float shortEdge, int w, int h, NumeralFont numerals)
    {
        EnsureBackground(dial, w, h);

        int master = (int)Math.Clamp(Math.Round(ClockController.ClassicFraction * shortEdge), 160, 2600);
        int layer = (int)Math.Ceiling(master * 1.22);
        string key = $"{dial.Id}|{lume.Id}|{layer}|{lumeCap:F2}|{numerals}";
        if (key == _cacheKey && _day != null) return;
        _numerals = numerals;

        _day?.Dispose(); _night?.Dispose(); _spec?.Dispose();
        _layer = layer;
        _masterR = master / 2f;
        _cacheKey = key;

        _day = NewLayer();
        _night = NewLayer();
        _spec = NewLayer();
        BuildFace(_day, dial, lume, lumeCap, night: false);
        BuildFace(_night, dial, lume, lumeCap, night: true);
        BuildSpecular(_spec);
    }

    private ID2D1Bitmap1 NewLayer() =>
        _dc.CreateBitmap(new SizeI(_layer, _layer), IntPtr.Zero, 0, new BitmapProperties1(Pf, 96, 96, BitmapOptions.Target));

    private void BuildFace(ID2D1Bitmap1 bmp, DialTheme d, LumeColor lume, float lumeCap, bool night)
    {
        float R = _masterR, c = _layer / 2f;
        var faceMid = Rgba.Lerp(d.FaceCenter, d.FaceEdge, 0.5f);
        var nightFace = Rgba.Lerp(faceMid, Rgba.Black, d.NightDim);
        var lumeColor = lume.Color.Scale(lumeCap);

        var ink = night ? lumeColor : d.Ink;
        float numAlpha = night ? 0.95f : 1f;
        float minNumAlpha = night ? 0.62f : 0.88f;
        float tickAlpha = night ? 0.50f : 0.80f;

        using var pillShadow = Record(() => DrawPills(c, R, PillStyle.Silhouette, d, nightFace, lumeColor));
        using var glow = night ? Record(() =>
        {
            DrawPills(c, R, PillStyle.GrooveOnly, d, nightFace, lumeColor);
            DrawHourNumerals(c, R, lumeColor, 1f);
            DrawMinuteNumerals(c, R, lumeColor, 0.65f);
            DrawTicks(c, R, lumeColor, 0.55f);
        }) : null;

        _dc.Target = bmp;
        _dc.BeginDraw();
        _dc.Transform = Matrix3x2.Identity;
        _dc.Clear(new Color4(0, 0, 0, 0));

        DrawShadow(pillShadow, R * 0.0065f, new Vector2(R * 0.009f, R * 0.016f), night ? 0.50f : d.ShadowStrength * 0.95f);
        DrawPills(c, R, night ? PillStyle.Night : PillStyle.Day, d, nightFace, lumeColor);
        DrawTicks(c, R, ink, tickAlpha);
        DrawHourNumerals(c, R, ink, numAlpha);
        DrawMinuteNumerals(c, R, ink, minNumAlpha);

        if (night && glow != null)
        {
            using var tight = new GaussianBlur(_dc);
            tight.SetInput(0, glow, true);
            tight.StandardDeviation = R * 0.010f;
            using var wide = new GaussianBlur(_dc);
            wide.SetInput(0, glow, true);
            wide.StandardDeviation = R * 0.040f;
            using var o1 = new Opacity(_dc);
            o1.SetInput(0, tight.Output, true);
            o1.Value = 0.80f;
            _dc.DrawImage(o1, InterpolationMode.Linear, CompositeMode.Plus);
            using var o2 = new Opacity(_dc);
            o2.SetInput(0, wide.Output, true);
            o2.Value = 0.50f;
            _dc.DrawImage(o2, InterpolationMode.Linear, CompositeMode.Plus);
        }

        _dc.EndDraw();
        _dc.Target = null;
    }

    private void BuildSpecular(ID2D1Bitmap1 bmp)
    {
        float R = _masterR, c = _layer / 2f;
        _dc.Target = bmp;
        _dc.BeginDraw();
        _dc.Transform = Matrix3x2.Identity;
        _dc.Clear(new Color4(0, 0, 0, 0));
        DrawPillHighlights(c, R);
        _dc.EndDraw();
        _dc.Target = null;
    }

    // ---- dial furniture

    private enum PillStyle { Day, Night, Silhouette, GrooveOnly }

    private static (float Length, float Width) PillSize(int i) =>
        i % 3 == 0 ? (PillLength * QuarterScale, PillWidth * QuarterScale) : (PillLength, PillWidth);

    /// <summary>The raised 5-minute pills: a light body, a thin rim, and an inset groove.</summary>
    private void DrawPills(float c, float R, PillStyle style, DialTheme d, Rgba nightFace, Rgba lume)
    {
        for (int i = 0; i < 12; i++)
        {
            var (len, wid) = PillSize(i);
            float L = len * R, W = wid * R, mid = PillCenter * R;
            _dc.Transform = Matrix3x2.CreateRotation(i * MathF.PI / 6f) * Matrix3x2.CreateTranslation(c, c);

            float x0 = -W / 2, x1 = W / 2, y0 = -(mid + L / 2), y1 = -(mid - L / 2);
            float inset = W * 0.20f;
            var groove = new RawRectF(x0 + inset, y0 + inset * 1.15f, x1 - inset, y1 - inset * 1.15f);
            float gr = (groove.Right - groove.Left) / 2f;
            var outer = new RoundedRectangle(new RawRectF(x0, y0, x1, y1), W / 2, W / 2);
            var inner = new RoundedRectangle(groove, gr, gr);

            switch (style)
            {
                case PillStyle.Silhouette:
                    _brush.Color = new Color4(0, 0, 0, 1);
                    _dc.FillRoundedRectangle(outer, _brush);
                    break;

                case PillStyle.Day:
                    _brush.Color = ToColor(d.Hand);
                    _dc.FillRoundedRectangle(outer, _brush);
                    _brush.Color = ToColor(d.Ink, 0.16f);
                    _dc.DrawRoundedRectangle(outer, _brush, R * 0.0030f);
                    // recessed groove: dial-tinted, with a darker upper-left lip and a bright lower-right lip
                    _brush.Color = ToColor(Rgba.Lerp(d.Hand, Rgba.Lerp(d.FaceCenter, d.FaceEdge, 0.5f), 0.30f));
                    _dc.FillRoundedRectangle(inner, _brush);
                    _brush.Color = new Color4(0, 0, 0, 0.20f);
                    _dc.DrawRoundedRectangle(inner, _brush, R * 0.0034f);
                    break;

                case PillStyle.Night:
                    _brush.Color = ToColor(Rgba.Lerp(nightFace, lume, 0.10f).Scale(1.25f));
                    _dc.FillRoundedRectangle(outer, _brush);
                    _brush.Color = ToColor(lume, 0.30f);
                    _dc.DrawRoundedRectangle(outer, _brush, R * 0.0030f);
                    _brush.Color = ToColor(lume);
                    _dc.FillRoundedRectangle(inner, _brush);
                    break;

                case PillStyle.GrooveOnly:
                    _brush.Color = ToColor(lume);
                    _dc.FillRoundedRectangle(inner, _brush);
                    break;
            }
        }
        _dc.Transform = Matrix3x2.Identity;
    }

    private void DrawTicks(float c, float R, Rgba color, float alpha)
    {
        for (int m = 0; m < 60; m++)
        {
            if (m % 5 == 0) continue;
            _dc.Transform = Matrix3x2.CreateRotation(m * MathF.PI / 30f) * Matrix3x2.CreateTranslation(c, c);
            float half = TickLength * R / 2, w = TickWidth * R / 2, mid = TickCenter * R;
            _brush.Color = ToColor(color, alpha);
            _dc.FillRoundedRectangle(new RoundedRectangle(new RawRectF(-w, -(mid + half), w, -(mid - half)), w, w), _brush);
        }
        _dc.Transform = Matrix3x2.Identity;
    }

    private void DrawHourNumerals(float c, float R, Rgba color, float alpha)
    {
        for (int h = 1; h <= 12; h++)
        {
            double a = h * Math.PI / 6;
            float x = c + (float)Math.Sin(a) * HourNumRadius * R, y = c - (float)Math.Cos(a) * HourNumRadius * R;
            DrawNumber(h.ToString(), x, y, HourNumHeight * R, 0f, color, alpha);
        }
    }

    /// <summary>60, 05, 10 ... 55 around the rim, standing on the radius, flipped on the lower half so they never read upside-down.</summary>
    private void DrawMinuteNumerals(float c, float R, Rgba color, float alpha)
    {
        for (int i = 0; i < 12; i++)
        {
            int minute = i * 5;
            string label = minute == 0 ? "60" : minute.ToString("00");
            double deg = i * 30.0;
            double a = deg * Math.PI / 180;
            float x = c + (float)Math.Sin(a) * MinNumRadius * R, y = c - (float)Math.Cos(a) * MinNumRadius * R;
            double rot = deg > 90 && deg < 270 ? deg + 180 : deg;
            DrawNumber(label, x, y, MinNumHeight * R, (float)(rot * Math.PI / 180), color, alpha, small: true);
        }
    }

    /// <summary>Draw digits centred on (cx, cy). <paramref name="height"/> is the visible digit height in pixels.</summary>
    private void DrawNumber(string text, float cx, float cy, float height, float rotation, Rgba color, float alpha, bool small = false)
    {
        var lib = FontLibrary.Instance;
        if (_numerals != NumeralFont.Klock && lib != null && lib.Has(_numerals))
        {
            using var run = lib.CreateRun(_numerals, text, height * FontScale(_numerals, small));
            _brush.Color = ToColor(color, alpha);
            _dc.Transform = Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(cx, cy);
            _dc.DrawTextLayout(run.Origin, run.Layout, _brush);
            _dc.Transform = Matrix3x2.Identity;
            return;
        }

        // Built-in face (also the fallback when a font cannot be loaded)
        float unit = height / (1f + Glyphs.Stroke);
        _brush.Color = ToColor(color, alpha);
        foreach (var (ch, x) in Glyphs.Layout(text))
        {
            // skeleton space -> centred on the string -> scaled -> rotated -> placed
            _dc.Transform = Matrix3x2.CreateTranslation(x, -0.5f) * Matrix3x2.CreateScale(unit)
                            * Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(cx, cy);
            _dc.DrawGeometry(_glyphs.Get(ch), _brush, Glyphs.Stroke, _glyphs.StrokeStyle);
        }
        _dc.Transform = Matrix3x2.Identity;
    }

    /// <summary>Optical size correction so each typeface sits at the same visual weight on the dial.</summary>
    /// <param name="small">True for the minute ring, where heavy faces need to be smaller to stay open.</param>
    private static float FontScale(NumeralFont f, bool small) => (f, small) switch
    {
        (NumeralFont.Panchang, false) => 0.80f,
        (NumeralFont.Panchang, true) => 0.74f,
        (NumeralFont.ClashDisplay, false) => 1.02f,
        (NumeralFont.ClashDisplay, true) => 1.00f,
        (NumeralFont.Satoshi, false) => 1.00f,
        (NumeralFont.Satoshi, true) => 0.95f,
        (NumeralFont.Melodrama, false) => 1.00f,
        (NumeralFont.Melodrama, true) => 1.02f,
        _ => 1f,
    };

    /// <summary>Bright upper-left lip on every pill (breathes with the burn-in highlight scale).</summary>
    private void DrawPillHighlights(float c, float R)
    {
        float d = R * 0.0042f;
        for (int i = 0; i < 12; i++)
        {
            var (len, wid) = PillSize(i);
            using var shape = PillGeometry(i, c, R, len, wid, Vector2.Zero);
            using var shifted = PillGeometry(i, c, R, len, wid, new Vector2(d, d));
            using var crescent = Crescent(shape, shifted);
            _brush.Color = new Color4(1, 1, 1, 0.85f);
            _dc.FillGeometry(crescent, _brush);
        }
    }

    private ID2D1TransformedGeometry PillGeometry(int i, float c, float R, float len, float wid, Vector2 globalOffset)
    {
        float L = len * R, W = wid * R, mid = PillCenter * R;
        using var rr = _dev.D2DFactory.CreateRoundedRectangleGeometry(
            new RoundedRectangle(new RawRectF(-W / 2, -(mid + L / 2), W / 2, -(mid - L / 2)), W / 2, W / 2));
        var m = Matrix3x2.CreateRotation(i * MathF.PI / 6f) * Matrix3x2.CreateTranslation(c + globalOffset.X, c + globalOffset.Y);
        return _dev.D2DFactory.CreateTransformedGeometry(rr, m);
    }

    /// <summary>The part of <paramref name="a"/> not covered by <paramref name="b"/>.</summary>
    private ID2D1PathGeometry Crescent(ID2D1Geometry a, ID2D1Geometry b)
    {
        var path = _dev.D2DFactory.CreatePathGeometry();
        using var sink = path.Open();
        a.CombineWithGeometry(b, CombineMode.Exclude, 0.1f, sink);
        sink.Close();
        return path;
    }

    // ------------------------------------------------------------------ primitives

    private void Fill(ID2D1Geometry g, Rgba color, float alpha = 1f)
    {
        _brush.Color = new Color4(color.R, color.G, color.B, color.A * alpha);
        _dc.FillGeometry(g, _brush);
    }

    private void Stroke(ID2D1Geometry g, Rgba color, float alpha, float width)
    {
        _brush.Color = new Color4(color.R, color.G, color.B, color.A * alpha);
        _dc.DrawGeometry(g, _brush, width);
    }

    private void Disc(float x, float y, float r, Rgba color, float alpha = 1f)
    {
        _brush.Color = new Color4(color.R, color.G, color.B, color.A * alpha);
        _dc.FillEllipse(new Ellipse(new Vector2(x, y), r, r), _brush);
    }

    private void Ring(float x, float y, float r, float stroke, Rgba color, float alpha)
    {
        _brush.Color = new Color4(color.R, color.G, color.B, color.A * alpha);
        _dc.DrawEllipse(new Ellipse(new Vector2(x, y), r, r), _brush, stroke);
    }

    private static Matrix3x2 Rotation(double deg, float cx, float cy) =>
        Matrix3x2.CreateRotation((float)(deg * Math.PI / 180.0)) * Matrix3x2.CreateTranslation(cx, cy);

    private static Color4 ToColor(Rgba c, float alpha = 1f) => new(c.R, c.G, c.B, c.A * alpha);

    /// <summary>Record drawing commands into a command list (must not be called inside BeginDraw).</summary>
    private ID2D1CommandList Record(Action draw)
    {
        var cl = _dc.CreateCommandList();
        _dc.Target = cl;
        _dc.BeginDraw();
        _dc.Transform = Matrix3x2.Identity;
        draw();
        _dc.EndDraw();
        _dc.Target = null;
        cl.Close();
        return cl;
    }

    /// <summary>Paper-like grain: fixed-seed noise, lightly smoothed so it reads as fibre, not TV static.</summary>
    private void CreateGrain()
    {
        const int n = 256;
        var rng = new Random(0x4B4C4F43); // fixed seed: identical grain everywhere
        var v = new float[n * n];
        for (int i = 0; i < v.Length; i++) v[i] = (float)rng.NextDouble() - 0.5f;

        var smooth = new float[v.Length];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sum = v[y * n + x] * 2f;
                sum += v[y * n + (x + 1) % n] + v[y * n + (x + n - 1) % n] + v[((y + 1) % n) * n + x] + v[((y + n - 1) % n) * n + x];
                smooth[y * n + x] = sum / 6f * 2.2f; // re-normalise contrast after smoothing
            }

        var px = new byte[n * n * 4];
        for (int i = 0; i < smooth.Length; i++)
        {
            float s = Math.Clamp(smooth[i], -0.5f, 0.5f);
            byte a = (byte)(Math.Abs(s) * 2f * 255f);
            byte col = (byte)(s > 0 ? a : 0); // premultiplied: light fibres or dark fibres
            px[i * 4 + 0] = col; px[i * 4 + 1] = col; px[i * 4 + 2] = col; px[i * 4 + 3] = a;
        }
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try
        {
            _grain = _dc.CreateBitmap(new SizeI(n, n), handle.AddrOfPinnedObject(), n * 4,
                new BitmapProperties1(Pf, 96, 96, BitmapOptions.None));
        }
        finally { handle.Free(); }
        _grainBrush = _dc.CreateBitmapBrush(_grain,
            new BitmapBrushProperties1(ExtendMode.Wrap, ExtendMode.Wrap, InterpolationMode.Linear));
    }
}




