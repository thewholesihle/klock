using System.Runtime.InteropServices;
using Klock.Core;
using Klock.Core.Hosting;
using Klock.Core.Settings;
using Klock.Renderer.Native;

namespace Klock.Screensaver;

internal static class Program
{
    private static int Main(string[] argv)
    {
        // A screensaver must never leave the user stuck on a black screen: log, restore the cursor, and exit.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Logger.Error("Unhandled exception", e.ExceptionObject as Exception);
            try { while (NativeMethods.ShowCursor(true) < 0) { } } catch { }
            Environment.Exit(1);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { Logger.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        try
        {
            NativeMethods.EnablePerMonitorDpi();
            var args = LaunchArgs.Parse(argv);
            Logger.Info($"Start {AppInfo.Name} {AppInfo.Version}: {string.Join(' ', argv)} => {args.Mode}");

            switch (args.Mode)
            {
                case LaunchMode.Screensaver:
                    return RunScreensaver();
                case LaunchMode.PreviewPane:
                    return PreviewPaneHost.Run(args.Hwnd, new SettingsStore().Load());
                case LaunchMode.Configure:
                    return SettingsLauncher.Launch();
                case LaunchMode.Demo:
                    return new ClockHost(ClockHost.Kind.Demo, new SettingsStore().Load(), args.Has("--fullscreen")).Run();
                case LaunchMode.Capture:
                    UseConsole();
                    return DevCommands.Capture(args);
                case LaunchMode.Sheet:
                    UseConsole();
                    return DevCommands.Sheet(args);
                case LaunchMode.Icon:
                    UseConsole();
                    return DevCommands.Icon(args);
                case LaunchMode.Bench:
                    UseConsole();
                    return DevCommands.Bench(args);
                default:
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error", ex);
            try { while (NativeMethods.ShowCursor(true) < 0) { } } catch { }
            return 1;
        }
    }

    private static int RunScreensaver()
    {
        // Single-instance guard: Windows can fire /s twice (e.g. idle timer racing "Preview").
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Klock.Screensaver.Running", out bool created);
        if (!created) { Logger.Info("Already running; exiting"); return 0; }
        try
        {
            // KLOCK_AUTOEXIT_SECONDS lets QA scripts run /s unattended; never set in normal use.
            double auto = double.TryParse(Environment.GetEnvironmentVariable("KLOCK_AUTOEXIT_SECONDS"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0;
            return new ClockHost(ClockHost.Kind.Screensaver, new SettingsStore().Load()).Run(auto);
        }
        finally { mutex.ReleaseMutex(); }
    }

    /// <summary>WinExe has no console; attach to the parent shell so dev commands can print.</summary>
    private static void UseConsole()
    {
        NativeMethods.AttachConsole(-1);
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(stdout);
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }
}

