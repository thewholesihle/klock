namespace Klock.Renderer;

/// <summary>
/// Dependency-free animated GIF encoder for marketing captures: one global palette (median cut over a sample of
/// every frame), 4x4 ordered dithering so soft gradients do not band, LZW compression, looping forever.
/// </summary>
public static class GifWriter
{
    public static void Save(string path, IReadOnlyList<byte[]> framesBgra, int width, int height, int fps)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        Write(fs, framesBgra, width, height, fps);
    }

    public static void Write(Stream s, IReadOnlyList<byte[]> frames, int width, int height, int fps)
    {
        var palette = BuildPalette(frames);
        var lut = new Dictionary<int, byte>(4096);

        using var w = new BinaryWriter(s, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("GIF89a"u8);
        w.Write((ushort)width); w.Write((ushort)height);
        w.Write((byte)0xF7); // global colour table, 8 bits per channel resolution, 256 entries
        w.Write((byte)0); w.Write((byte)0);
        foreach (var (r, g, b) in palette) { w.Write(r); w.Write(g); w.Write(b); }

        // Loop forever
        w.Write((byte)0x21); w.Write((byte)0xFF); w.Write((byte)11); w.Write("NETSCAPE2.0"u8);
        w.Write((byte)3); w.Write((byte)1); w.Write((ushort)0); w.Write((byte)0);

        ushort delay = (ushort)Math.Max(2, (int)Math.Round(100.0 / fps)); // centiseconds
        var indices = new byte[width * height];
        foreach (var frame in frames)
        {
            Quantize(frame, width, height, palette, lut, indices);
            w.Write((byte)0x21); w.Write((byte)0xF9); w.Write((byte)4); w.Write((byte)0x04); // graphic control, dispose: keep
            w.Write(delay); w.Write((byte)0); w.Write((byte)0);
            w.Write((byte)0x2C); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)width); w.Write((ushort)height); w.Write((byte)0);
            w.Write((byte)8); // LZW minimum code size
            WriteSubBlocks(w, Lzw(indices));
        }
        w.Write((byte)0x3B);
    }

    private static void Quantize(byte[] bgra, int width, int height, (byte R, byte G, byte B)[] pal,
        Dictionary<int, byte> lut, byte[] outIdx)
    {
        ReadOnlySpan<int> bayer = stackalloc int[] { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                int d = (bayer[(y & 3) * 4 + (x & 3)] - 8) / 2; // about +/-4 levels of dither
                int r = Math.Clamp(bgra[i + 2] + d, 0, 255), g = Math.Clamp(bgra[i + 1] + d, 0, 255), b = Math.Clamp(bgra[i] + d, 0, 255);
                int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                if (!lut.TryGetValue(key, out var idx))
                {
                    idx = Nearest(pal, (r & 0xF8) | 4, (g & 0xF8) | 4, (b & 0xF8) | 4);
                    lut[key] = idx;
                }
                outIdx[y * width + x] = idx;
            }
    }

    private static byte Nearest((byte R, byte G, byte B)[] pal, int r, int g, int b)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < pal.Length; i++)
        {
            int dr = pal[i].R - r, dg = pal[i].G - g, db = pal[i].B - b;
            int d = dr * dr * 3 + dg * dg * 4 + db * db * 2; // rough perceptual weighting
            if (d < bestD) { bestD = d; best = i; }
        }
        return (byte)best;
    }

    // ---- palette: median cut over a sample ----

    private static (byte R, byte G, byte B)[] BuildPalette(IReadOnlyList<byte[]> frames)
    {
        var sample = new List<(byte R, byte G, byte B)>();
        int step = Math.Max(1, (int)(frames.Sum(f => (long)f.Length / 4) / 120_000));
        foreach (var f in frames)
            for (int i = 0; i < f.Length; i += 4 * step) sample.Add((f[i + 2], f[i + 1], f[i]));

        var boxes = new List<List<(byte R, byte G, byte B)>> { sample };
        while (boxes.Count < 256)
        {
            // split the box with the widest channel range
            int bi = -1, bestRange = 0, bestCh = 0;
            for (int k = 0; k < boxes.Count; k++)
            {
                if (boxes[k].Count < 2) continue;
                for (int ch = 0; ch < 3; ch++)
                {
                    int lo = 255, hi = 0;
                    foreach (var p in boxes[k]) { int v = Ch(p, ch); if (v < lo) lo = v; if (v > hi) hi = v; }
                    if (hi - lo > bestRange) { bestRange = hi - lo; bi = k; bestCh = ch; }
                }
            }
            if (bi < 0 || bestRange == 0) break;
            var box = boxes[bi];
            box.Sort((a, b) => Ch(a, bestCh).CompareTo(Ch(b, bestCh)));
            int mid = box.Count / 2;
            boxes[bi] = box.GetRange(0, mid);
            boxes.Add(box.GetRange(mid, box.Count - mid));
        }

        var pal = new (byte, byte, byte)[256];
        for (int i = 0; i < 256; i++)
        {
            if (i >= boxes.Count || boxes[i].Count == 0) { pal[i] = (0, 0, 0); continue; }
            pal[i] = ((byte)(boxes[i].Average(p => p.R)), (byte)(boxes[i].Average(p => p.G)), (byte)(boxes[i].Average(p => p.B)));
        }
        return pal;
    }

    private static int Ch((byte R, byte G, byte B) p, int ch) => ch == 0 ? p.R : ch == 1 ? p.G : p.B;

    // ---- LZW ----

    private static byte[] Lzw(byte[] data)
    {
        const int clear = 256, eoi = 257;
        var output = new List<byte>(data.Length / 2);
        int bitBuf = 0, bitCnt = 0, codeSize = 9, next = 258;
        var dict = new Dictionary<(int, byte), int>();

        void Emit(int code)
        {
            bitBuf |= code << bitCnt;
            bitCnt += codeSize;
            while (bitCnt >= 8) { output.Add((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCnt -= 8; }
        }

        Emit(clear);
        int cur = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            byte k = data[i];
            if (dict.TryGetValue((cur, k), out int code)) { cur = code; continue; }
            Emit(cur);
            if (next < 4096)
            {
                dict[(cur, k)] = next++;
                if (next - 1 == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clear);
                dict.Clear();
                next = 258;
                codeSize = 9;
            }
            cur = k;
        }
        Emit(cur);
        Emit(eoi);
        if (bitCnt > 0) output.Add((byte)(bitBuf & 0xFF));
        return output.ToArray();
    }

    private static void WriteSubBlocks(BinaryWriter w, byte[] data)
    {
        for (int i = 0; i < data.Length; i += 255)
        {
            int n = Math.Min(255, data.Length - i);
            w.Write((byte)n);
            w.Write(data, i, n);
        }
        w.Write((byte)0);
    }
}
