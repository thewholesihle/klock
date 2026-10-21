using System.Windows.Media;
using System.Windows.Media.Imaging;
using Klock.Core;
using Klock.Core.Burnin;
using Klock.Core.Themes;
using Klock.Core.Time;
using Klock.Renderer;

namespace Klock.Settings;

/// <summary>Miniature dial swatches, rendered by the real renderer rather than approximated in XAML.</summary>
public static class DialThumbnails
{
    public const int Size = 144; // rendered at 2x for crisp display at 72 DIPs

    public static Dictionary<string, BitmapSource> Render()
    {
        var dials = DialCatalog.All;
        var frames = dials.Select(d => new ClockFrame
        {
            Dial = d,
            Lume = LumeCatalog.Default,
            Night = 0,
            Hands = MovementEngine.Evaluate(MovementType.Digital, 10 * 3600 + 9 * 60 + 36),
            BurnIn = PixelShift.Evaluate(0, Size, BurnInLevel.Off),
            SizeFraction = 0.98,
            Background = Rgba.Transparent,
            MatchDialBackground = true,
        });

        var result = new Dictionary<string, BitmapSource>();
        int i = 0;
        foreach (var px in OffscreenRenderer.RenderSequence(frames, Size, Size))
        {
            var bmp = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Pbgra32, null, px, Size * 4);
            bmp.Freeze();
            result[dials[i++].Id] = bmp;
        }
        return result;
    }
}

