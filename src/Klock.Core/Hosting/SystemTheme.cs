using Microsoft.Win32;

namespace Klock.Core.Hosting;

public static class SystemTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The Windows "app mode": true = light, false = dark, null if it cannot be read.</summary>
    public static bool? AppsUseLightTheme()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("AppsUseLightTheme") is int v ? v != 0 : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
