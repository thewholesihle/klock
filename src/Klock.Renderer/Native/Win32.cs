using System.Runtime.InteropServices;

namespace Klock.Renderer.Native;

public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam, lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WNDCLASSEX
{
    public uint cbSize;
    public uint style;
    public WndProc lpfnWndProc;
    public int cbClsExtra, cbWndExtra;
    public IntPtr hInstance, hIcon, hCursor, hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFOEX
{
    public uint cbSize;
    public RECT rcMonitor, rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
}

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
    public uint BatteryLifeTime, BatteryFullLifeTime;
}

public static class Wm
{
    public const uint NCCREATE = 0x0081, NCDESTROY = 0x0082, DESTROY = 0x0002, CLOSE = 0x0010, SIZE = 0x0005;
    public const uint ACTIVATEAPP = 0x001C, SETCURSOR = 0x0020, ERASEBKGND = 0x0014, PAINT = 0x000F;
    public const uint KEYDOWN = 0x0100, SYSKEYDOWN = 0x0104, SYSCOMMAND = 0x0112, TIMER = 0x0113;
    public const uint MOUSEMOVE = 0x0200, LBUTTONDOWN = 0x0201, RBUTTONDOWN = 0x0204, MBUTTONDOWN = 0x0207, MOUSEWHEEL = 0x020A;
    public const uint XBUTTONDOWN = 0x020B;
    public const uint TIMECHANGE = 0x001E, SETTINGCHANGE = 0x001A, POWERBROADCAST = 0x0218, WTSSESSION_CHANGE = 0x02B1;
    public const uint DISPLAYCHANGE = 0x007E, DPICHANGED = 0x02E0;
    public const uint APP = 0x8000;

    public const int SC_SCREENSAVE = 0xF140, SC_CLOSE = 0xF060, SC_MONITORPOWER = 0xF170;
    public const int PBT_APMSUSPEND = 0x4, PBT_APMRESUMEAUTOMATIC = 0x12, PBT_APMRESUMESUSPEND = 0x7, PBT_POWERSETTINGCHANGE = 0x8013;
    public const int WTS_SESSION_LOCK = 0x7, WTS_SESSION_UNLOCK = 0x8;
}

public static class Ws
{
    public const uint POPUP = 0x80000000, CHILD = 0x40000000, VISIBLE = 0x10000000, OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint CLIPCHILDREN = 0x02000000, CLIPSIBLINGS = 0x04000000;
    public const uint EX_TOPMOST = 0x00000008, EX_TOOLWINDOW = 0x00000080, EX_NOACTIVATE = 0x08000000;
}

public readonly record struct MonitorInfo(IntPtr Handle, int X, int Y, int Width, int Height, string DeviceName, bool IsPrimary);

public static class NativeMethods
{
    private const string User32 = "user32.dll", Kernel32 = "kernel32.dll";

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassExW(ref WNDCLASSEX c);
    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr hInst, IntPtr param);
    [DllImport(User32, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool DestroyWindow(IntPtr h);
    [DllImport(User32)] public static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport(User32)] public static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport(User32)] public static extern bool TranslateMessage(ref MSG m);
    [DllImport(User32)] public static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport(User32)] public static extern void PostQuitMessage(int code);
    [DllImport(User32)] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport(User32)] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport(User32)] public static extern bool UpdateWindow(IntPtr h);
    [DllImport(User32)] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport(User32)] public static extern IntPtr SetFocus(IntPtr h);
    [DllImport(User32)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int h2, uint flags);
    [DllImport(User32)] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport(User32)] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport(User32)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr h);
    [DllImport(User32)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out POINT p);
    [DllImport(User32)] public static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
    [DllImport(User32)] public static extern IntPtr SetCursor(IntPtr c);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
    [DllImport(User32)] public static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr proc);
    [DllImport(User32)] public static extern bool KillTimer(IntPtr h, UIntPtr id);
    [DllImport(User32)] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport(User32)] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport(User32, SetLastError = true)] public static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, uint flags);
    [DllImport(User32)] public static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSRegisterSessionNotification(IntPtr h, uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr h);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
    [DllImport(User32)] public static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
    [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int obj);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern int MessageBoxW(IntPtr h, string text, string caption, uint type);
    [DllImport(Kernel32, CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string? name);
    [DllImport(Kernel32)] public static extern bool AttachConsole(int pid);
    [DllImport(Kernel32)] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport(User32)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport(User32, CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr hMon, ref MONITORINFOEX info);

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOW = 5, SW_SHOWNOACTIVATE = 4;
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr DPI_PER_MONITOR_V2 = new(-4);
    public static readonly Guid GuidConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    public static void EnablePerMonitorDpi() => SetProcessDpiAwarenessContext(DPI_PER_MONITOR_V2);

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data) =>
        {
            var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoW(hMon, ref info))
            {
                var r = info.rcMonitor;
                list.Add(new MonitorInfo(hMon, r.Left, r.Top, r.Width, r.Height, info.szDevice, (info.dwFlags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        // Primary first, then left-to-right: stable ordering for "per-monitor" settings UI.
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.X).ThenBy(m => m.Y).ToList();
    }

    public static bool IsOnBattery() =>
        GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;

    public static (int Width, int Height) GetClientSize(IntPtr hwnd) =>
        GetClientRect(hwnd, out var r) ? (r.Width, r.Height) : (0, 0);
}

