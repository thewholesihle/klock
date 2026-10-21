using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace Klock.Settings;

/// <summary>Resource lookup for the (localised) settings UI. The clock itself is wordless.</summary>
public static class Loc
{
    private static readonly ResourceManager Rm = new("Klock.Settings.Resources.Strings", typeof(Loc).Assembly);

    public static readonly (string Code, string NativeName)[] Languages =
    {
        ("en", "English"), ("fr", "Français"), ("de", "Deutsch"), ("es", "Español"), ("it", "Italiano"),
        ("ja", "日本語"), ("zh-Hans", "简体中文"), ("ru", "Русский"), ("tr", "Türkçe"), ("ar", "العربية"),
    };

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public static bool IsRtl => Culture.TextInfo.IsRightToLeft;

    /// <param name="setting">"auto" (system language) or a culture name.</param>
    public static void SetLanguage(string? setting)
    {
        Culture = string.IsNullOrWhiteSpace(setting) || setting == "auto"
            ? CultureInfo.InstalledUICulture
            : SafeCulture(setting);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    private static CultureInfo SafeCulture(string name)
    {
        try { return CultureInfo.GetCultureInfo(name); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en"); }
    }

    public static string Get(string key) => Rm.GetString(key, Culture) ?? key;

    public static string Format(string key, params object[] args) => string.Format(Culture, Get(key), args);
}

/// <summary>XAML: <c>Text="{local:T section.dial}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public TExtension() { }
    public TExtension(string key) => Key = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
