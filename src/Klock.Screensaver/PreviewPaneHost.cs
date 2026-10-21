using Klock.Core;
using Klock.Core.Hosting;
using Klock.Core.Settings;
using Klock.Renderer;
using Klock.Renderer.Native;

namespace Klock.Screensaver;

/// <summary>
/// <c>/p HWND</c>: render the clock live into the small preview pane of the Windows Screen Saver Settings
/// dialog as a child window. Exits when the dialog (the parent) goes away.
/// </summary>
internal static class PreviewPaneHost
{
    private const uint PollTimerId = 7;
    private const int PreviewFpsCap = 30;

    public static int Run(long parentHandle, KlockSettings settings)
    {
        var parent = (IntPtr)parentHandle;
        if (!NativeMethods.IsWindow(parent)) return 0;

        var (w, h) = NativeMethods.GetClientSize(parent);
        var child = Win32Window.Create(Ws.CHILD | Ws.VISIBLE | Ws.CLIPCHILDREN | Ws.CLIPSIBLINGS, 0, 0, 0,
            Math.Max(1, w), Math.Max(1, h), parent, "KlockPreview");

        var controller = new ClockController(settings, appsUseLightTheme: SystemTheme.AppsUseLightTheme);
        using var view = new ClockView(child.Handle, controller, fpsCap: () => PreviewFpsCap, adapter: () => settings.GpuAdapter);
        view.Failed += _ => child.Post(Wm.CLOSE);

        child.Hook = (msg, wp, lp) =>
        {
            switch (msg)
            {
                case Wm.SIZE:
                    view.Resize((int)((long)lp & 0xFFFF), (int)(((long)lp >> 16) & 0xFFFF));
                    return null;
                case Wm.TIMER when (uint)wp == PollTimerId:
                    // The dialog does not always send WM_CLOSE; poll for its disappearance.
                    if (!NativeMethods.IsWindow(parent))
                    {
                        NativeMethods.KillTimer(child.Handle, (UIntPtr)PollTimerId);
                        Win32Window.Quit();
                    }
                    return IntPtr.Zero;
                case Wm.CLOSE or Wm.DESTROY:
                    Win32Window.Quit();
                    return null;
            }
            return null;
        };

        NativeMethods.SetTimer(child.Handle, (UIntPtr)PollTimerId, 500, IntPtr.Zero);
        child.Show(activate: false);
        view.Start();
        Win32Window.RunMessageLoop();
        view.Stop();
        child.Dispose();
        return 0;
    }
}

