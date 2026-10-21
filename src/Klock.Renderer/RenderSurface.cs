using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Klock.Renderer;

/// <summary>A place the painter can draw: a window swap chain or an offscreen bitmap.</summary>
public abstract class RenderSurface : IDisposable
{
    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public abstract ID2D1Bitmap1 Target { get; }
    public abstract void Dispose();

    protected static readonly Vortice.DCommon.PixelFormat Bgra =
        new(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
}

public sealed class OffscreenSurface : RenderSurface
{
    private readonly ID2D1DeviceContext _dc;
    private readonly ID2D1Bitmap1 _target;

    public OffscreenSurface(ID2D1DeviceContext dc, int width, int height)
    {
        _dc = dc;
        Width = width;
        Height = height;
        _target = dc.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0,
            new BitmapProperties1(Bgra, 96, 96, BitmapOptions.Target));
    }

    public override ID2D1Bitmap1 Target => _target;

    /// <summary>Copies the surface to CPU memory as top-down BGRA, premultiplied, tightly packed.</summary>
    public byte[] ReadPixels()
    {
        using var staging = _dc.CreateBitmap(new SizeI(Width, Height), IntPtr.Zero, 0,
            new BitmapProperties1(Bgra, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        staging.CopyFromBitmap(_target);
        var map = staging.Map(MapOptions.Read);
        try
        {
            var data = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(map.Bits + y * (int)map.Pitch, data, y * Width * 4, Width * 4);
            return data;
        }
        finally { staging.Unmap(); }
    }

    public override void Dispose() => _target.Dispose();
}

public enum PresentResult { Ok, DeviceLost, Occluded }

/// <summary>Flip-model swap chain on an HWND. Present(1) blocks to vsync, pacing each window to its own monitor.</summary>
public sealed class SwapChainSurface : RenderSurface
{
    private readonly RenderDevice _dev;
    private readonly ID2D1DeviceContext _dc;
    private readonly IDXGISwapChain1 _swap;
    private ID2D1Bitmap1? _target;

    public SwapChainSurface(RenderDevice dev, ID2D1DeviceContext dc, IntPtr hwnd, int width, int height)
    {
        _dev = dev;
        _dc = dc;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        var desc = new SwapChainDescription1
        {
            Width = (uint)Width,
            Height = (uint)Height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.None,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };
        _swap = dev.Factory.CreateSwapChainForHwnd(dev.D3D, hwnd, desc);
        // Keep the shell from toggling exclusive fullscreen on us.
        dev.Factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAll);
        CreateTarget();
    }

    public override ID2D1Bitmap1 Target => _target ?? throw new ObjectDisposedException(nameof(SwapChainSurface));

    private void CreateTarget()
    {
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _target = _dc.CreateBitmapFromDxgiSurface(surface,
            new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore),
                96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;

        _dc.Target = null;
        _target?.Dispose();
        _target = null;
        _swap.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
        Width = width;
        Height = height;
        CreateTarget();
    }

    /// <param name="syncInterval">Present every Nth vertical blank (1 = every refresh, 0 = immediately).</param>
    public PresentResult Present(int syncInterval)
    {
        Result r = _swap.Present((uint)Math.Clamp(syncInterval, 0, 4), PresentFlags.None);
        if (r == Vortice.DXGI.ResultCode.DeviceRemoved || r == Vortice.DXGI.ResultCode.DeviceReset)
            return PresentResult.DeviceLost;
        if (r.Code == 0x087A0001) // DXGI_STATUS_OCCLUDED
            return PresentResult.Occluded;
        return r.Failure ? PresentResult.DeviceLost : PresentResult.Ok;
    }

    public override void Dispose()
    {
        _dc.Target = null;
        _target?.Dispose();
        _swap.Dispose();
    }
}

