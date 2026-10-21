namespace Klock.Core;

/// <summary>Single place for the product identity so the working title is trivially renamed.</summary>
public static class AppInfo
{
    public const string Name = "Klock";
    public const string RegistryKeyPath = @"Software\" + Name;
    public const string Version = "0.1.0";
    /// <summary>GitHub "owner/repo" that hosts the releases. Replace OWNER before the first public release.</summary>
    public const string GitHubRepo = "OWNER/klock";
    public const string ReleasesUrl = "https://github.com/" + GitHubRepo + "/releases/latest";
    public const string SupportUrl = "https://github.com/" + GitHubRepo + "/issues";
    /// <summary>latest.json is attached to every GitHub release; build.ps1 generates it next to the installers.</summary>
    public const string UpdateManifestUrl = "https://github.com/" + GitHubRepo + "/releases/latest/download/latest.json";

    public static string LocalDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

    public static string LogDir => Path.Combine(LocalDataDir, "logs");
}
