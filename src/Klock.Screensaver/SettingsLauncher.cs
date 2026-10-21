using System.Diagnostics;
using Klock.Core;
using Klock.Renderer.Native;
using Microsoft.Win32;

namespace Klock.Screensaver;

/// <summary>
/// <c>/c</c> from the Screen Saver Settings dialog. The .scr lives in System32 while the settings
/// app lives in the install folder, so the installer records <c>InstallDir</c> in the registry.
/// </summary>
internal static class SettingsLauncher
{
    public const string SettingsExe = "Klock.Settings.exe";

    public static int Launch()
    {
        var path = Find();
        if (path == null)
        {
            Logger.Warn("Settings app not found");
            NativeMethods.MessageBoxW(IntPtr.Zero,
                $"{AppInfo.Name} settings could not be found. Please reinstall {AppInfo.Name}.",
                AppInfo.Name, 0x10 /* MB_ICONERROR */);
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start settings app", ex);
            return 1;
        }
    }

    private static string? Find()
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, SettingsExe) };
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = hive.OpenSubKey(AppInfo.RegistryKeyPath);
                if (k?.GetValue("InstallDir") is string dir) candidates.Add(Path.Combine(dir, SettingsExe));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
