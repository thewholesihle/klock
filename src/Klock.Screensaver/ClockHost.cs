using System.Diagnostics;
using System.Runtime.InteropServices;
using Klock.Core;
using Klock.Core.Hosting;
using Klock.Core.Settings;
using Klock.Core.Time;
using Klock.Renderer;
using Klock.Renderer.Audio;
using Klock.Renderer.Native;

namespace Klock.Screensaver;

/// <summary>
/// Full-screen (/s) and windowed demo (--preview, --bench) host: one window and one render thread per
/// monitor, shared time source, live monitor hot-plug, pause on display-off/lock, resync on resume/time change.
/// </summary>
internal sealed class ClockHost
{
    public enum Kind { Screensaver, Demo }

    private sealed class MonitorWindow
    {
        public required Win32Window Window { get; init; }
        public ClockView? View { get; init; }
        public required MonitorInfo Info { get; set; }
    }

    private const uint ExitTimerId = 1;

    private readonly Kind _kind;
    private readonly KlockSettings _settings;
    private readonly SmoothClock _clock = SmoothClock.CreateSystem();
    private readonly ExitInputDetector _input = new();
    private readonly Stopwatch _since = Stopwatch.StartNew();
    private readonly Dictionary<string, MonitorWindow> _windows = new();
    private readonly List<ClockController> _controllers = new();
    private readonly bool _fullscreenDemo;
    private readonly bool _forceSoftware;

    private Win32Window? _owner;
    private TickScheduler? _ticks;
    private IntPtr _powerNotify;
    private bool _displayOff, _locked, _exiting, _wasActive;
    private long _batteryTick;
    private bool _onBattery;

    public ClockHost(Kind kind, KlockSettings settings, bool fullscreenDemo = false, bool forceSoftware = false)
    {
        _kind = kind;
        _settings = settings;
        _fullscreenDemo = fullscreenDemo;
        _forceSoftware = forceSoftware;
    }

    public IEnumerable<ClockView> Views => _windows.Values.Where(w => w.View != null).Select(w => w.View!);

    /// <param name="autoExitSeconds">For --bench: quit after this long.</param>
    public int Run(double autoExitSeconds = 0)
    {
        NativeMethods.timeBeginPeriod(1);
        try
        {
            SyncMonitors();
            if (_owner == null) throw new InvalidOperationException("No monitors available");

            if (_kind == Kind.Screensaver)
            {
                while (NativeMethods.ShowCursor(false) >= 0) { }
                RegisterNotifications(_owner.Handle);
                NativeMethods.SetForegroundWindow(_owner.Handle);
            }
            // One tick scheduler for the whole machine (not one per monitor), locked to the shared clock.
            _ticks = new TickScheduler(_clock.Now, () => _settings.Movement, () => _settings.TickSound, () => _displayOff || _locked);

            if (autoExitSeconds > 0)
                NativeMethods.SetTimer(_owner.Handle, (UIntPtr)ExitTimerId, (uint)(autoExitSeconds * 1000), IntPtr.Zero);

            Win32Window.RunMessageLoop();
            return 0;
        }
        finally
        {
            Shutdown();
            NativeMethods.timeEndPeriod(1);
        }
    }

    // ------------------------------------------------------------------ monitors

    private IEnumerable<MonitorInfo> ChooseMonitors(IReadOnlyList<MonitorInfo> all)
    {
        if (_kind == Kind.Demo)
            return new[] { all.FirstOrDefault(m => m.IsPrimary, all[0]) };
        return all;
    }

    private bool ShowsClock(MonitorInfo m, IReadOnlyList<MonitorInfo> all)
    {
        if (_kind == Kind.Demo) return true;
        switch (_settings.Monitors)
        {
            case MonitorMode.PrimaryOnly:
                return m.IsPrimary;
            case MonitorMode.Selected:
                bool any = all.Any(x => _settings.SelectedMonitors.Contains(x.DeviceName));
                // If none of the saved monitors exist any more (hot-unplug), fall back to primary rather than blank everything.
                return any ? _settings.SelectedMonitors.Contains(m.DeviceName) : m.IsPrimary;
            default:
                return true;
        }
    }

    private void SyncMonitors()
    {
        var all = NativeMethods.GetMonitors();
        if (all.Count == 0) return;
        var wanted = ChooseMonitors(all).ToList();

        // Remove vanished monitors
        foreach (var gone in _windows.Keys.Where(k => wanted.All(m => m.DeviceName != k)).ToList())
        {
            var w = _windows[gone];
            _windows.Remove(gone);
            w.View?.Stop();
            if (w.Window != _owner) w.Window.Dispose();
        }

        foreach (var m in wanted)
        {
            if (_windows.TryGetValue(m.DeviceName, out var existing))
            {
                if (existing.Info != m)
                {
                    existing.Info = m;
                    if (_kind == Kind.Screensaver) existing.Window.Move(m.X, m.Y, m.Width, m.Height, topmost: true);
                }
                continue;
            }
            _windows[m.DeviceName] = CreateMonitorWindow(m, ShowsClock(m, all));
        }

        _owner ??= _windows.Values.FirstOrDefault()?.Window;
    }

    private MonitorWindow CreateMonitorWindow(MonitorInfo m, bool showClock)
    {
        Win32Window win;
        if (_kind == Kind.Screensaver)
        {
            win = Win32Window.Create(Ws.POPUP, Ws.EX_TOPMOST | Ws.EX_TOOLWINDOW, m.X, m.Y, m.Width, m.Height,
                title: "Klock", arrowCursor: false);
        }
        else if (_fullscreenDemo)
        {
            win = Win32Window.Create(Ws.POPUP, Ws.EX_TOOLWINDOW, m.X, m.Y, m.Width, m.Height, title: "Klock");
        }
        else
        {
            int w = Math.Min(1280, m.Width * 3 / 4), h = Math.Min(800, m.Height * 3 / 4);
            win = Win32Window.Create(Ws.OVERLAPPEDWINDOW, 0, m.X + (m.Width - w) / 2, m.Y + (m.Height - h) / 2, w, h, title: "Klock");
        }
        win.PaintBlack = !showClock;

        ClockView? view = null;
        if (showClock)
        {
            var controller = new ClockController(_settings, _clock, SystemTheme.AppsUseLightTheme);
            _controllers.Add(controller);
            string device = m.DeviceName;
            view = new ClockView(win.Handle, controller,
                fpsCap: () => FrameRatePolicy.EffectiveCap(_settings, OnBattery()),
                monitorOffset: () => _settings.MonitorOffsets.GetValueOrDefault(device),
                adapter: () => _settings.GpuAdapter,
                forceSoftware: _forceSoftware);
            view.Failed += ex =>
            {
                Logger.Error("Rendering failed permanently; ending screensaver", ex);
                if (_owner != null) _owner.Post(Wm.CLOSE);
            };
        }

        var mw = new MonitorWindow { Window = win, View = view, Info = m };
        win.Hook = (msg, wp, lp) => OnMessage(mw, msg, wp, lp);
        win.Show(activate: _kind == Kind.Demo);
        view?.Start();
        return mw;
    }

    // ------------------------------------------------------------------ messages

    private IntPtr? OnMessage(MonitorWindow mw, uint msg, IntPtr wParam, IntPtr lParam)
    {
        bool saver = _kind == Kind.Screensaver;
        switch (msg)
        {
            case Wm.SETCURSOR when saver:
                NativeMethods.SetCursor(IntPtr.Zero);
                return (IntPtr)1;

            case Wm.KEYDOWN or Wm.SYSKEYDOWN:
                if (saver) { if (_input.OnDiscreteInput(_since.Elapsed.TotalSeconds)) Exit(); }
                else if ((int)wParam == 0x1B) Exit(); // Esc
                return null;

            case Wm.LBUTTONDOWN or Wm.RBUTTONDOWN or Wm.MBUTTONDOWN or Wm.XBUTTONDOWN or Wm.MOUSEWHEEL when saver:
                if (_input.OnDiscreteInput(_since.Elapsed.TotalSeconds)) Exit();
                return null;

            case Wm.MOUSEMOVE when saver:
                if (NativeMethods.GetCursorPos(out var p) && _input.OnMouseMove(p.X, p.Y, _since.Elapsed.TotalSeconds)) Exit();
                return null;

            case Wm.ACTIVATEAPP when saver:
                if (wParam != IntPtr.Zero) _wasActive = true;
                else if (_wasActive && _since.Elapsed.TotalSeconds > 1.0) Exit();
                return null;

            case Wm.SYSCOMMAND:
                int cmd = (int)wParam & 0xFFF0;
                if (cmd == Wm.SC_SCREENSAVE && saver) return IntPtr.Zero; // we are the screensaver already
                if (cmd == Wm.SC_CLOSE && saver) { Exit(); return IntPtr.Zero; }
                return null;

            case Wm.SIZE:
                mw.View?.Resize((int)((long)lParam & 0xFFFF), (int)(((long)lParam >> 16) & 0xFFFF));
                return null;

            case Wm.DISPLAYCHANGE when mw.Window == _owner:
                Logger.Info("Display configuration changed");
                SyncMonitors();
                return null;

            case Wm.TIMECHANGE when mw.Window == _owner:
                Logger.Info("System time changed; resyncing");
                _clock.Snap();
                return null;

            case Wm.POWERBROADCAST when mw.Window == _owner:
                OnPowerBroadcast((int)wParam, lParam);
                return null;

            case Wm.WTSSESSION_CHANGE when mw.Window == _owner:
                if ((int)wParam == Wm.WTS_SESSION_LOCK) { _locked = true; ApplyPause(); }
                else if ((int)wParam == Wm.WTS_SESSION_UNLOCK) { _locked = false; _clock.Snap(); ApplyPause(); }
                return null;

            case Wm.TIMER when (uint)wParam == ExitTimerId:
                NativeMethods.KillTimer(mw.Window.Handle, (UIntPtr)ExitTimerId);
                Exit();
                return IntPtr.Zero;

            case Wm.CLOSE:
                Exit();
                return IntPtr.Zero;

            case Wm.DESTROY when mw.Window == _owner:
                Win32Window.Quit();
                return null;
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    private void OnPowerBroadcast(int event_, IntPtr lParam)
    {
        switch (event_)
        {
            case Wm.PBT_APMRESUMEAUTOMATIC or Wm.PBT_APMRESUMESUSPEND:
                Logger.Info("Resumed from sleep; resyncing time");
                _clock.Snap();
                foreach (var c in _controllers) c.ResyncTime();
                break;
            case Wm.PBT_POWERSETTINGCHANGE when lParam != IntPtr.Zero:
                var s = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
                if (s.PowerSetting == NativeMethods.GuidConsoleDisplayState)
                {
                    _displayOff = s.Data == 0; // 0 = off, 1 = on, 2 = dimmed
                    ApplyPause();
                }
                break;
        }
    }

    private void ApplyPause()
    {
        bool pause = _displayOff || _locked;
        foreach (var v in Views) v.SetPaused(pause);
        Logger.Info(pause ? "Rendering paused (display off / locked)" : "Rendering resumed");
    }

    private bool OnBattery()
    {
        long t = Environment.TickCount64;
        if (t - _batteryTick > 5000)
        {
            _batteryTick = t;
            _onBattery = NativeMethods.IsOnBattery();
        }
        return _onBattery;
    }

    private void RegisterNotifications(IntPtr hwnd)
    {
        NativeMethods.WTSRegisterSessionNotification(hwnd, 0 /* NOTIFY_FOR_THIS_SESSION */);
        var g = NativeMethods.GuidConsoleDisplayState;
        _powerNotify = NativeMethods.RegisterPowerSettingNotification(hwnd, ref g, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);
    }

    // ------------------------------------------------------------------ shutdown

    private void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        Win32Window.Quit();
    }

    private void Shutdown()
    {
        _ticks?.Dispose();
        foreach (var w in _windows.Values) w.View?.Stop();
        if (_powerNotify != IntPtr.Zero) NativeMethods.UnregisterPowerSettingNotification(_powerNotify);
        if (_owner != null) NativeMethods.WTSUnRegisterSessionNotification(_owner.Handle);
        foreach (var w in _windows.Values) w.Window.Dispose();
        if (_kind == Kind.Screensaver) while (NativeMethods.ShowCursor(true) < 0) { }
    }
}

