using Klock.Core.Burnin;
using Klock.Core.Settings;
using Klock.Core.Time;

namespace Klock.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klock-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _reg = @"Software\Klock.Tests\" + Guid.NewGuid().ToString("N");
    private SettingsStore Store() => new(_reg, Path.Combine(_dir, "settings.json"));

    public void Dispose()
    {
        Store().Delete();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Defaults_are_sensible()
    {
        var s = new KlockSettings();
        Assert.Equal("white", s.DialId);
        Assert.Equal(BurnInLevel.Standard, s.BurnIn);
        Assert.False(s.TickSound);
        Assert.Equal(ThemeMode.Auto, s.Theme);
    }

    [Fact]
    public void Json_roundtrip_preserves_everything()
    {
        var s = new KlockSettings
        {
            DialId = "ocean", Theme = ThemeMode.Schedule, LumeId = "amber", Movement = MovementType.Mechanical,
            Size = DialSize.Compact, BackgroundHex = "#101010", FpsCap = 120, BurnIn = BurnInLevel.Extended,
            Language = "ar", Latitude = 51.5, Longitude = -0.12, Monitors = MonitorMode.Selected,
            SelectedMonitors = { @"\\.\DISPLAY2" },
            MonitorOffsets = { [@"\\.\DISPLAY2"] = new MonitorOffset { X = 0.05, Y = -0.1 } },
        };
        var back = SettingsJson.Deserialize(SettingsJson.Serialize(s));
        Assert.Equal(SettingsJson.Serialize(s), SettingsJson.Serialize(back));
        Assert.Equal(MovementType.Mechanical, back.Movement);
        Assert.Equal(-0.1, back.MonitorOffsets[@"\\.\DISPLAY2"].Y);
    }

    [Fact]
    public void Enums_serialise_as_readable_strings()
    {
        var json = SettingsJson.Serialize(new KlockSettings { Movement = MovementType.Digital });
        Assert.Contains("\"Digital\"", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    public void Corrupt_json_yields_defaults_without_throwing(string? json)
    {
        var s = SettingsJson.Deserialize(json);
        Assert.Equal("white", s.DialId);
    }

    [Fact]
    public void Out_of_range_values_are_repaired()
    {
        var s = SettingsJson.Deserialize("""
            { "Theme": 99, "BackgroundHex": "nonsense", "FpsCap": 100000, "BatteryFpsCap": 1,
              "Latitude": 400, "NightStart": "bogus",
              "MonitorOffsets": { "a": { "X": 5, "Y": -5 } } }
            """);
        Assert.Equal(ThemeMode.Auto, s.Theme);
        Assert.Equal("#000000", s.BackgroundHex);
        Assert.Equal(360, s.FpsCap);
        Assert.Equal(10, s.BatteryFpsCap);
        Assert.Null(s.Latitude);
        Assert.Equal("19:00", s.NightStart);
        Assert.Equal(0.25, s.MonitorOffsets["a"].X);
        Assert.Equal(-0.25, s.MonitorOffsets["a"].Y);
    }

    [Fact]
    public void Unknown_fields_and_missing_fields_are_tolerated()
    {
        var s = SettingsJson.Deserialize("""{ "DialId": "noir", "FutureThing": 42 }""");
        Assert.Equal("noir", s.DialId);
        Assert.Equal(MovementType.Quartz, s.Movement);
    }

    [Fact]
    public void Store_persists_to_registry_and_file_mirror()
    {
        var store = Store();
        store.Save(new KlockSettings { DialId = "rose" });
        Assert.Equal("rose", Store().Load().DialId);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Store_falls_back_to_file_when_registry_value_is_missing()
    {
        var store = Store();
        store.Save(new KlockSettings { DialId = "slate" });
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_reg, writable: true))
            k!.DeleteValue(SettingsStore.ValueName);
        Assert.Equal("slate", Store().Load().DialId);
    }

    [Fact]
    public void Load_with_nothing_stored_returns_defaults() => Assert.Equal("white", Store().Load().DialId);

    [Fact]
    public void Older_schema_is_reset_but_language_survives()
    {
        var s = SettingsJson.Deserialize("""
            { "SchemaVersion": 0, "DialId": "ocean", "Movement": "Mechanical", "Language": "fr", "Size": "Compact" }
            """);
        Assert.Equal("white", s.DialId);
        Assert.Equal(KlockSettings.CurrentSchema, s.SchemaVersion);
        Assert.Equal("fr", s.Language);
        Assert.Equal(DialSize.Classic, s.Size);
    }

    [Fact]
    public void Settings_files_from_licensing_builds_still_load()
    {
        // An earlier build stored a LicenseKey; the field no longer exists and must be ignored, not break loading.
        var s = SettingsJson.Deserialize("""{ "DialId": "ocean", "LicenseKey": "KLK1.a.b" }""");
        Assert.Equal("ocean", s.DialId);
    }

    [Fact]
    public void Current_schema_is_left_alone()
    {
        var s = SettingsJson.Deserialize($$"""{ "SchemaVersion": {{KlockSettings.CurrentSchema}}, "DialId": "ocean" }""");
        Assert.Equal("ocean", s.DialId);
    }

    [Fact]
    public void Clone_is_deep()
    {
        var a = new KlockSettings();
        a.SelectedMonitors.Add("x");
        var b = a.Clone();
        b.SelectedMonitors.Add("y");
        Assert.Single(a.SelectedMonitors);
    }
}
