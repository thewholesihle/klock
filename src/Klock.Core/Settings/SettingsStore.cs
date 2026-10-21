using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Klock.Core.Settings;

/// <summary>Reads the numeral font by name; unknown or retired names become <see cref="Themes.NumeralFont.Panchang"/>.</summary>
internal sealed class LenientNumeralFontConverter : JsonConverter<Themes.NumeralFont>
{
    public override Themes.NumeralFont Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<Themes.NumeralFont>(reader.GetString(), ignoreCase: true, out var f) && Enum.IsDefined(f))
            return f;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(Themes.NumeralFont), n))
            return (Themes.NumeralFont)n;
        reader.Skip();
        return Themes.NumeralFont.Panchang;
    }

    public override void Write(Utf8JsonWriter writer, Themes.NumeralFont value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public static class SettingsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Numeral typeface first: settings saved by an older build may name a font that no longer exists
        // (for example "Boxing"). That must fall back to the default, never discard the user's other settings.
        Converters = { new LenientNumeralFontConverter(), new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(KlockSettings s) => JsonSerializer.Serialize(s, Options);

    /// <summary>Never throws: corrupt or partial JSON yields defaults for whatever could not be read.</summary>
    public static KlockSettings Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new KlockSettings();
        try
        {
            return (JsonSerializer.Deserialize<KlockSettings>(json, Options) ?? new KlockSettings()).Normalize().MigrateToCurrent();
        }
        catch (JsonException)
        {
            return new KlockSettings();
        }
    }
}

/// <summary>
/// Settings live in <c>HKCU\Software\Klock</c> (value <c>SettingsJson</c>) with a JSON mirror in
/// <c>%LOCALAPPDATA%\Klock\settings.json</c> used as a fallback when the registry is unavailable.
/// </summary>
public sealed class SettingsStore
{
    public const string ValueName = "SettingsJson";

    private readonly string _registryPath;
    private readonly string _filePath;

    public SettingsStore(string? registryPath = null, string? filePath = null)
    {
        _registryPath = registryPath ?? AppInfo.RegistryKeyPath;
        _filePath = filePath ?? Path.Combine(AppInfo.LocalDataDir, "settings.json");
    }

    public string FilePath => _filePath;

    public KlockSettings Load()
    {
        var json = ReadRegistry() ?? ReadFile();
        return SettingsJson.Deserialize(json);
    }

    public void Save(KlockSettings settings)
    {
        var json = SettingsJson.Serialize(settings.Normalize());
        bool reg = WriteRegistry(json);
        // Always mirror to disk; it is the sole store if the registry write failed.
        WriteFile(json, required: !reg);
    }

    private string? ReadRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private bool WriteRegistry(string json)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue(ValueName, json, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private string? ReadFile()
    {
        try { return File.Exists(_filePath) ? File.ReadAllText(_filePath) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void WriteFile(string json, bool required)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            // Write-then-replace so a crash never leaves a truncated file.
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (required) throw;
        }
    }

    /// <summary>Remove all persisted settings (used by tests and the uninstaller).</summary>
    public void Delete()
    {
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_registryPath, throwOnMissingSubKey: false); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch (IOException) { }
    }
}
