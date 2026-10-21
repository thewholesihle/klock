using System.Text;

namespace Klock.Core;

/// <summary>Tiny append-only file logger. Never throws: logging must not be able to break a screensaver.</summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string? _dirOverride;

    public static void SetDirectory(string dir) => _dirOverride = dir;

    public static string CurrentFile =>
        Path.Combine(_dirOverride ?? AppInfo.LogDir, $"klock-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = CurrentFile;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path,
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
                TrimOld(Path.GetDirectoryName(path)!);
            }
        }
        catch
        {
            // Intentionally swallowed.
        }
    }

    private static DateTime _lastTrim = DateTime.MinValue;

    private static void TrimOld(string dir)
    {
        if (DateTime.UtcNow - _lastTrim < TimeSpan.FromHours(6)) return;
        _lastTrim = DateTime.UtcNow;
        foreach (var f in Directory.GetFiles(dir, "klock-*.log"))
            if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14)) File.Delete(f);
    }
}
