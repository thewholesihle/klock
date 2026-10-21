using System.Text.RegularExpressions;
using Klock.Core.Themes;

namespace Klock.Tests;

/// <summary>Guards the translation sources in tools/strings so a missing key can never ship.</summary>
public class LocalizationTests
{
    private static readonly string[] Cultures = { "fr", "de", "es", "it", "ja", "zh-Hans", "ru", "tr", "ar" };

    private static string StringsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "strings"))) dir = dir.Parent;
        return dir == null ? throw new DirectoryNotFoundException("tools/strings not found") : Path.Combine(dir.FullName, "tools", "strings");
    }

    private static Dictionary<string, string> Load(string culture)
    {
        var d = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(Path.Combine(StringsDir(), culture + ".txt")))
        {
            if (line.TrimStart().StartsWith('#') || !line.Contains('=')) continue;
            int i = line.IndexOf('=');
            Assert.True(d.TryAdd(line[..i].Trim(), line[(i + 1)..]), $"{culture}: duplicate key {line[..i]}");
        }
        return d;
    }

    [Fact]
    public void English_defines_every_dial_and_lume_name()
    {
        var en = Load("en");
        foreach (var d in DialCatalog.All) Assert.True(en.ContainsKey(d.NameKey), $"missing {d.NameKey}");
        foreach (var l in LumeCatalog.All) Assert.True(en.ContainsKey(l.NameKey), $"missing {l.NameKey}");
    }

    [Theory]
    [MemberData(nameof(AllCultures))]
    public void Every_language_has_every_key_and_no_extras(string culture)
    {
        var en = Load("en");
        var tr = Load(culture);
        Assert.Empty(en.Keys.Except(tr.Keys));
        Assert.Empty(tr.Keys.Except(en.Keys));
    }

    [Theory]
    [MemberData(nameof(AllCultures))]
    public void Placeholders_match_english_and_nothing_is_blank(string culture)
    {
        var en = Load("en");
        var tr = Load(culture);
        foreach (var (key, english) in en)
        {
            var translated = tr[key];
            Assert.False(string.IsNullOrWhiteSpace(translated), $"{culture}/{key} is blank");
            Assert.Equal(Placeholders(english), Placeholders(translated));
        }
    }

    [Fact]
    public void There_are_exactly_ten_languages_including_rtl_arabic()
    {
        Assert.Equal(9, Cultures.Length); // + English neutral
        Assert.True(File.Exists(Path.Combine(StringsDir(), "ar.txt")));
    }

    private static string Placeholders(string s) =>
        string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x));

    public static IEnumerable<object[]> AllCultures() => Cultures.Select(c => new object[] { c });
}
