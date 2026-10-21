using System.Runtime.InteropServices;

namespace Klock.Renderer.Native;

/// <summary>
/// Thin Win32 window wrapper. Must be created and destroyed on the thread that pumps its messages.
/// A <see cref="Hook"/> sees every message first; return a value to mark it handled.
/// </summary>
public sealed class Win32Window : IDisposable
{
    private const string ClassName = "KlockClockWindow";
    private static readonly object RegLock = new();
    private static bool _registered;
    private static readonly WndProc Proc = StaticWndProc; // rooted for the life of the process
    private static readonly Dictionary<IntPtr, Win32Window> Map = new();
    [ThreadStatic] private static Win32Window? _creating;

    public delegate IntPtr? MessageHook(uint msg, IntPtr wParam, IntPtr lParam);

    public IntPtr Handle { get; private set; }
    public MessageHook? Hook { get; set; }
    /// <summary>Fill with GDI black instead of expecting a GPU surface (used for monitors the clock is not shown on).</summary>
    public bool PaintBlack { get; set; }

    private Win32Window() { }

    public static Win32Window Create(uint style, uint exStyle, int x, int y, int width, int height,
        IntPtr parent = default, string title = "", bool arrowCursor = true)
    {
        EnsureRegistered(arrowCursor);
        var w = new Win32Window();
        _creating = w;
        try
        {
            var h = NativeMethods.CreateWindowExW(exStyle, ClassName, title, style, x, y, width, height,
                parent, IntPtr.Zero, NativeMethods.GetModuleHandleW(null), IntPtr.Zero);
            if (h == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            w.Handle = h;
            lock (Map) Map[h] = w;
            return w;
        }
        finally { _creating = null; }
    }

    public void Show(bool activate = true)
    {
        NativeMethods.ShowWindow(Handle, activate ? NativeMethods.SW_SHOW : NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.UpdateWindow(Handle);
    }

    public void Move(int x, int y, int w, int h, bool topmost = false) =>
        NativeMethods.SetWindowPos(Handle, topmost ? NativeMethods.HWND_TOPMOST : IntPtr.Zero, x, y, w, h,
            NativeMethods.SWP_NOACTIVATE | (topmost ? 0u : NativeMethods.SWP_NOZORDER));

    public (int Width, int Height) ClientSize => NativeMethods.GetClientSize(Handle);

    public void Post(uint msg, IntPtr w = default, IntPtr l = default) => NativeMethods.PostMessageW(Handle, msg, w, l);

    /// <summary>Run the message loop on the calling thread until <see cref="Quit"/>.</summary>
    public static int RunMessageLoop()
    {
        while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }
        return 0;
    }

    public static void Quit(int code = 0) => NativeMethods.PostQuitMessage(code);

    public void Dispose()
    {
        var h = Handle;
        Handle = IntPtr.Zero;
        if (h != IntPtr.Zero && NativeMethods.IsWindow(h)) NativeMethods.DestroyWindow(h);
    }

    private static void EnsureRegistered(bool arrowCursor)
    {
        lock (RegLock)
        {
            if (_registered) return;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Proc,
                hInstance = NativeMethods.GetModuleHandleW(null),
                hCursor = arrowCursor ? NativeMethods.LoadCursorW(IntPtr.Zero, new IntPtr(32512)) : IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszClassName = ClassName,
            };
            if (NativeMethods.RegisterClassExW(ref wc) == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _registered = true;
        }
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        Win32Window? w;
        lock (Map) Map.TryGetValue(hWnd, out w);
        if (w == null && _creating != null && msg == Wm.NCCREATE)
        {
            w = _creating;
            w.Handle = hWnd;
            lock (Map) Map[hWnd] = w;
        }

        if (w?.Hook != null)
        {
            try
            {
                var r = w.Hook(msg, wParam, lParam);
                if (r.HasValue) return r.Value;
            }
            catch (Exception ex)
            {
                Klock.Core.Logger.Error($"WndProc hook failed for message 0x{msg:X}", ex);
            }
        }

        if (msg == Wm.NCDESTROY)
        {
            lock (Map) Map.Remove(hWnd);
        }
        if (msg == Wm.ERASEBKGND)
        {
            if (w is { PaintBlack: true } && NativeMethods.GetClientRect(hWnd, out var rc))
                NativeMethods.FillRect(wParam, ref rc, NativeMethods.GetStockObject(4 /* BLACK_BRUSH */));
            return (IntPtr)1; // otherwise the swap chain owns every pixel
        }
        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
