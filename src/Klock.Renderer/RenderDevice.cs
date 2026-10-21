using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Klock.Renderer;

/// <summary>
/// D3D11 + D2D1 device stack. One per window (keeps device-lost recovery and threading simple;
/// the clock's GPU footprint is tiny). Falls back to WARP software rendering when no GPU works.
/// </summary>
public sealed class RenderDevice : IDisposable
{
    public ID3D11Device D3D { get; }
    public ID3D11DeviceContext D3DContext { get; }
    public IDXGIDevice1 Dxgi { get; }
    public IDXGIFactory2 Factory { get; }
    public ID2D1Factory1 D2DFactory { get; }
    public ID2D1Device D2D { get; }
    public bool IsSoftware { get; }
    public string AdapterName { get; }

    private RenderDevice(ID3D11Device d3d, ID3D11DeviceContext ctx, IDXGIFactory2 factory, ID2D1Factory1 d2dFactory, bool software)
    {
        D3D = d3d;
        D3DContext = ctx;
        Factory = factory;
        D2DFactory = d2dFactory;
        IsSoftware = software;
        Dxgi = d3d.QueryInterface<IDXGIDevice1>();
        Dxgi.MaximumFrameLatency = 1;
        using (var adapter = Dxgi.GetAdapter())
            AdapterName = adapter.Description.Description;
        D2D = d2dFactory.CreateDevice(Dxgi);
    }

    /// <param name="preferredAdapter">Adapter description (see <see cref="ListAdapters"/>); null = system default.</param>
    /// <param name="forceSoftware">Use WARP regardless of GPU availability (tests/headless).</param>
    public static RenderDevice Create(string? preferredAdapter = null, bool forceSoftware = false)
    {
        var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        var d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);

        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        const DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;

        if (!forceSoftware)
        {
            IDXGIAdapter1? chosen = null;
            if (!string.IsNullOrWhiteSpace(preferredAdapter))
            {
                for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? a).Success; i++)
                {
                    if (a!.Description1.Description == preferredAdapter) { chosen = a; break; }
                    a.Dispose();
                }
            }

            try
            {
                Result r = D3D11.D3D11CreateDevice(chosen, chosen == null ? DriverType.Hardware : DriverType.Unknown,
                    flags, levels, out ID3D11Device? dev, out ID3D11DeviceContext? ctx);
                chosen?.Dispose();
                if (r.Success && dev != null && ctx != null)
                    return new RenderDevice(dev, ctx, factory, d2dFactory, software: false);
            }
            catch (SharpGenException) { chosen?.Dispose(); }
        }

        Result w = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp, flags, levels, out ID3D11Device? warp, out ID3D11DeviceContext? wctx);
        w.CheckError();
        return new RenderDevice(warp!, wctx!, factory, d2dFactory, software: true);
    }

    public static IReadOnlyList<string> ListAdapters()
    {
        var names = new List<string>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? a).Success; i++)
            {
                using (a)
                {
                    var d = a!.Description1;
                    if ((d.Flags & AdapterFlags.Software) == 0) names.Add(d.Description);
                }
            }
        }
        catch (SharpGenException) { }
        return names;
    }

    public ID2D1DeviceContext CreateContext()
    {
        var dc = D2D.CreateDeviceContext(DeviceContextOptions.None);
        dc.SetDpi(96, 96);
        return dc;
    }

    public void Dispose()
    {
        D2D.Dispose();
        Dxgi.Dispose();
        D2DFactory.Dispose();
        Factory.Dispose();
        D3DContext.Dispose();
        D3D.Dispose();
    }
}

