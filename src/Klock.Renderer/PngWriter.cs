using System.Buffers.Binary;
using System.IO.Compression;

namespace Klock.Renderer;

/// <summary>Minimal dependency-free PNG encoder (8-bit RGB, no interlace).</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <param name="bgra">Top-down BGRA pixels, tightly packed.</param>
    public static void Save(string path, byte[] bgra, int width, int height)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, Encode(bgra, width, height));
    }

    public static byte[] Encode(byte[] bgra, int width, int height)
    {
        using var raw = new MemoryStream();
        var row = new byte[width * 3 + 1]; // filter byte 0 + RGB
        for (int y = 0; y < height; y++)
        {
            int src = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                row[1 + x * 3 + 0] = bgra[src + x * 4 + 2];
                row[1 + x * 3 + 1] = bgra[src + x * 4 + 1];
                row[1 + x * 3 + 2] = bgra[src + x * 4 + 0];
            }
            raw.Write(row, 0, row.Length);
        }

        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true))
            raw.WriteTo(zs);

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit, truecolour
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }


    /// <summary>RGBA PNG from premultiplied BGRA (un-premultiplies), for icons with transparency.</summary>
    public static byte[] EncodeRgba(byte[] pbgra, int width, int height)
    {
        using var raw = new MemoryStream();
        var row = new byte[width * 4 + 1];
        for (int y = 0; y < height; y++)
        {
            int src = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                byte b = pbgra[src + x * 4], g = pbgra[src + x * 4 + 1], r = pbgra[src + x * 4 + 2], a = pbgra[src + x * 4 + 3];
                if (a is > 0 and < 255) { r = (byte)Math.Min(255, r * 255 / a); g = (byte)Math.Min(255, g * 255 / a); b = (byte)Math.Min(255, b * 255 / a); }
                row[1 + x * 4] = r; row[2 + x * 4] = g; row[3 + x * 4] = b; row[4 + x * 4] = a;
            }
            raw.Write(row, 0, row.Length);
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(zs);

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }
    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var b in t) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(len, crc ^ 0xFFFFFFFF);
        s.Write(len);
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}

