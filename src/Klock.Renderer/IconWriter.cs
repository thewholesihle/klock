using System.Buffers.Binary;

namespace Klock.Renderer;

/// <summary>Builds a multi-resolution .ico (PNG-compressed entries, Vista+) from rendered images.</summary>
public static class IconWriter
{
    public static void Save(string path, IReadOnlyList<(int Size, byte[] PremultipliedBgra)> images)
    {
        var pngs = images.Select(i => (i.Size, Png: PngWriter.EncodeRgba(i.PremultipliedBgra, i.Size, i.Size))).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)pngs.Count);
        int offset = 6 + 16 * pngs.Count;
        foreach (var (size, png) in pngs)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(png.Length); w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in pngs) w.Write(png);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, ms.ToArray());
    }
}
