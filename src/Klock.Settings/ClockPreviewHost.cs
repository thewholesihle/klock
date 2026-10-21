using System.Runtime.InteropServices;
using System.Windows.Interop;
using Klock.Core;
using Klock.Renderer;
using Klock.Renderer.Native;

namespace Klock.Settings;

/// <summary>
/// Hosts the real clock renderer (the very same <see cref="ClockView"/> the screensaver uses) in a child
/// HWND, so the preview can never drift from what the screensaver shows.
/// </summary>
public sealed class ClockPreviewHost : HwndHost
{
    private readonly ClockController _controller;
    private readonly Func<string?> _adapter;
    private Win32Window? _window;
    private ClockView? _view;

    public ClockPreviewHost(ClockController controller, Func<string?> adapter)
    {
        _controller = controller;
        _adapter = adapter;
    }

    public ClockView? View => _view;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _window = Win32Window.Create(Ws.CHILD | Ws.VISIBLE | Ws.CLIPCHILDREN | Ws.CLIPSIBLINGS, 0, 0, 0, 16, 16,
            hwndParent.Handle, "KlockPreview");
        _window.Hook = (msg, _, lp) =>
        {
            if (msg == Wm.SIZE) _view?.Resize((int)((long)lp & 0xFFFF), (int)(((long)lp >> 16) & 0xFFFF));
            return null;
        };
        _view = new ClockView(_window.Handle, _controller, fpsCap: () => 60, adapter: _adapter);
        _view.Start();
        return new HandleRef(this, _window.Handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _view?.Stop();
        _view = null;
        _window?.Dispose();
        _window = null;
    }
}
