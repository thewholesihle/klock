using Klock.Core;

namespace Klock.Renderer;

/// <summary>Headless rendering for <c>--capture</c>, marketing stills and automated visual checks.</summary>
public static class OffscreenRenderer
{
    /// <summary>Render frames with a single device and painter (so caches are reused). Returns BGRA pixels per frame.</summary>
    public static IEnumerable<byte[]> RenderSequence(IEnumerable<ClockFrame> frames, int width, int height,
        bool forceSoftware = false, string? adapter = null)
    {
        using var dev = RenderDevice.Create(adapter, forceSoftware);
        using var dc = dev.CreateContext();
        using var painter = new ClockPainter(dev, dc);
        using var surface = new OffscreenSurface(dc, width, height);
        foreach (var f in frames)
        {
            painter.Paint(surface.Target, width, height, f);
            yield return surface.ReadPixels();
        }
    }

    public static byte[] RenderBgra(ClockFrame frame, int width, int height, bool forceSoftware = false) =>
        RenderSequence(new[] { frame }, width, height, forceSoftware).First();

    public static void RenderPng(string path, ClockFrame frame, int width, int height, bool forceSoftware = false) =>
        PngWriter.Save(path, RenderBgra(frame, width, height, forceSoftware), width, height);
}
