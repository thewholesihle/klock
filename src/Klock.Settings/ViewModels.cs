using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Klock.Core;
using Klock.Core.Burnin;
using Klock.Core.Settings;
using Klock.Core.Themes;
using Klock.Core.Time;
using Klock.Core.Updates;
using Klock.Renderer;
using Klock.Renderer.Audio;
using Klock.Renderer.Native;

namespace Klock.Settings;

public sealed record Choice<T>(T Value, string Label);

public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> run, Func<object?, bool>? can = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => can?.Invoke(p) ?? true;
    public void Execute(object? p) => run(p);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class DialItem(string id, string name) : ObservableBase
{
    private ImageSource? _thumb;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public ImageSource? Thumb { get => _thumb; set { _thumb = value; Raise(); } }
}

public sealed class LumeItem(string id, string name, Color color)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public Color Color { get; } = color;
    public Brush Brush { get; } = Freeze(new SolidColorBrush(color));
    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}

public sealed class BackgroundItem(string id)
{
    public string Id { get; } = id;
    public Brush Brush { get; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString(id));
}

/// <summary>One physical display: whether the clock shows on it and its position nudge.</summary>
public sealed class MonitorVm : ObservableBase
{
    private readonly SettingsViewModel _owner;
    public string DeviceName { get; }
    public string Title { get; }
    public bool IsPrimary { get; }

    public MonitorVm(SettingsViewModel owner, MonitorInfo info, int index)
    {
        _owner = owner;
        DeviceName = info.DeviceName;
        IsPrimary = info.IsPrimary;
        Title = Loc.Format("display.displayN", index + 1) + (info.IsPrimary ? Loc.Get("display.primarySuffix") : "") +
                $"  \u200E{info.Width}×{info.Height}\u200E"; // LRM keeps "1920×1080" in order inside RTL text
    }

    public bool Enabled
    {
        get => _owner.Settings.SelectedMonitors.Contains(DeviceName);
        set
        {
            var list = _owner.Settings.SelectedMonitors;
            if (value && !list.Contains(DeviceName)) list.Add(DeviceName);
            else if (!value) list.Remove(DeviceName);
            Raise();
            _owner.Touch();
        }
    }

    public double OffsetXPercent { get => Offset().X * 100; set { Offset().X = value / 100; Raise(); _owner.Touch(); } }
    public double OffsetYPercent { get => Offset().Y * 100; set { Offset().Y = value / 100; Raise(); _owner.Touch(); } }

    public bool HasOffset => Math.Abs(Offset().X) > 1e-6 || Math.Abs(Offset().Y) > 1e-6;

    private MonitorOffset Offset()
    {
        var d = _owner.Settings.MonitorOffsets;
        if (!d.TryGetValue(DeviceName, out var o)) d[DeviceName] = o = new MonitorOffset();
        return o;
    }

    public void ResetOffset()
    {
        _owner.Settings.MonitorOffsets.Remove(DeviceName);
        Raise(nameof(OffsetXPercent)); Raise(nameof(OffsetYPercent));
        _owner.Touch();
    }

    public void RaiseEnabled() => Raise(nameof(Enabled));
}

public sealed class SettingsViewModel : ObservableBase
{
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _saveTimer;
    private readonly Action _languageChanged;
    private CancellationTokenSource? _updateCts;

    public KlockSettings Settings { get; }
    public ClockController Preview { get; }

    public SettingsViewModel(SettingsStore store, KlockSettings settings, Action languageChanged)
    {
        _store = store;
        Settings = settings;
        _languageChanged = languageChanged;
        Preview = new ClockController(settings.Clone(), appsUseLightTheme: Klock.Core.Hosting.SystemTheme.AppsUseLightTheme);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Persist(); };

        Dials = DialCatalog.All.Select(d => new DialItem(d.Id, Loc.Get(d.NameKey))).ToList();
        Lumes = LumeCatalog.All.Select(l => new LumeItem(l.Id, Loc.Get(l.NameKey),
            Color.FromRgb((byte)(l.Color.R * 255), (byte)(l.Color.G * 255), (byte)(l.Color.B * 255)))).ToList();
        Backgrounds = new[] { "#000000", "#0B0B0D", "#101418", "#14110F", "#1C1C1E" }.Select(h => new BackgroundItem(h)).ToList();

        ThemeChoices = new[]
        {
            new Choice<ThemeMode>(ThemeMode.Auto, Loc.Get("theme.auto")), new(ThemeMode.Day, Loc.Get("theme.day")),
            new(ThemeMode.Night, Loc.Get("theme.night")), new(ThemeMode.Schedule, Loc.Get("theme.schedule")),
        };
        ScheduleChoices = new[]
        {
            new Choice<ScheduleKind>(ScheduleKind.CustomTimes, Loc.Get("schedule.kind.custom")),
            new(ScheduleKind.Solar, Loc.Get("schedule.kind.solar")),
        };
        MovementChoices = new[]
        {
            new Choice<MovementType>(MovementType.Quartz, Loc.Get("movement.quartz")),
            new(MovementType.Mechanical, Loc.Get("movement.mechanical")), new(MovementType.Digital, Loc.Get("movement.digital")),
        };
        SizeChoices = new[] { new Choice<DialSize>(DialSize.Classic, Loc.Get("size.classic")), new(DialSize.Compact, Loc.Get("size.compact")) };
        BurnInChoices = new[]
        {
            new Choice<BurnInLevel>(BurnInLevel.Off, Loc.Get("burnin.off")), new(BurnInLevel.Standard, Loc.Get("burnin.standard")),
            new(BurnInLevel.Extended, Loc.Get("burnin.extended")),
        };
        MonitorModeChoices = new[]
        {
            new Choice<MonitorMode>(MonitorMode.All, Loc.Get("display.all")), new(MonitorMode.PrimaryOnly, Loc.Get("display.primary")),
            new(MonitorMode.Selected, Loc.Get("display.selected")),
        };
        FpsChoices = new[] { 0, 30, 60, 90, 120, 144, 240 }
            .Select(f => new Choice<int>(f, f == 0 ? Loc.Get("perf.fps.auto") : Loc.Format("perf.fps.value", f))).ToList();
        BatteryFpsChoices = new[] { 15, 24, 30, 60 }.Select(f => new Choice<int>(f, Loc.Format("perf.fps.value", f))).ToList();
        GpuChoices = new List<Choice<string>> { new("", Loc.Get("perf.gpu.auto")) };
        foreach (var name in RenderDevice.ListAdapters()) GpuChoices.Add(new Choice<string>(name, name));
        LanguageChoices = new List<Choice<string>> { new("auto", Loc.Get("language.auto")) };
        foreach (var (code, native) in Loc.Languages) LanguageChoices.Add(new Choice<string>(code, native));

        Monitors = new ObservableCollection<MonitorVm>(NativeMethods.GetMonitors().Select((m, i) => new MonitorVm(this, m, i)));

        CheckUpdateCommand = new RelayCommand(async _ => await CheckForUpdatesAsync());
        OpenUpdateCommand = new RelayCommand(_ => Open(_updateUrl), _ => _updateUrl != null);
        SupportCommand = new RelayCommand(_ => Open(AppInfo.SupportUrl));
        ResetOffsetCommand = new RelayCommand(p => (p as MonitorVm)?.ResetOffset());
    }

    // ---------------------------------------------------------------- lists

    public IReadOnlyList<DialItem> Dials { get; }
    public IReadOnlyList<LumeItem> Lumes { get; }
    public IReadOnlyList<BackgroundItem> Backgrounds { get; }
    public IReadOnlyList<Choice<ThemeMode>> ThemeChoices { get; }
    public IReadOnlyList<Choice<ScheduleKind>> ScheduleChoices { get; }
    public IReadOnlyList<Choice<MovementType>> MovementChoices { get; }
    public IReadOnlyList<Choice<DialSize>> SizeChoices { get; }
    public IReadOnlyList<Choice<BurnInLevel>> BurnInChoices { get; }
    public IReadOnlyList<Choice<MonitorMode>> MonitorModeChoices { get; }
    public IReadOnlyList<Choice<int>> FpsChoices { get; }
    public IReadOnlyList<Choice<int>> BatteryFpsChoices { get; }
    public List<Choice<string>> GpuChoices { get; }
    public List<Choice<string>> LanguageChoices { get; }
    public ObservableCollection<MonitorVm> Monitors { get; }

    // ---------------------------------------------------------------- settings (apply instantly, persist on change)

    public string DialId { get => Settings.DialId; set { if (Settings.DialId != value && value != null) { Settings.DialId = value; Raise(); Touch(); } } }
    public string LumeId { get => Settings.LumeId; set { if (Settings.LumeId != value && value != null) { Settings.LumeId = value; Raise(); Touch(); } } }

    public ThemeMode Theme
    {
        get => Settings.Theme;
        set
        {
            if (Settings.Theme == value) return;
            Settings.Theme = value;
            Raise(); Raise(nameof(ThemeHint)); Raise(nameof(ShowSchedule));
            Raise(nameof(ShowCustomTimes)); Raise(nameof(ShowSolar));
            Touch();
        }
    }

    public string ThemeHint => Loc.Get(Settings.Theme switch
    {
        ThemeMode.Auto => "theme.auto.hint", ThemeMode.Day => "theme.day.hint",
        ThemeMode.Night => "theme.night.hint", _ => "theme.schedule.hint",
    });

    public bool ShowSchedule => Settings.Theme == ThemeMode.Schedule;
    public bool ShowCustomTimes => ShowSchedule && Settings.Schedule == ScheduleKind.CustomTimes;
    public bool ShowSolar => ShowSchedule && Settings.Schedule == ScheduleKind.Solar;

    public ScheduleKind Schedule
    {
        get => Settings.Schedule;
        set { if (Settings.Schedule == value) return; Settings.Schedule = value; Raise(); Raise(nameof(ShowCustomTimes)); Raise(nameof(ShowSolar)); Touch(); }
    }

    public string NightStart { get => Settings.NightStart; set { if (TimeSpan.TryParse(value, out var t)) { Settings.NightStart = $"{t:hh\\:mm}"; Raise(); Touch(); } } }
    public string NightEnd { get => Settings.NightEnd; set { if (TimeSpan.TryParse(value, out var t)) { Settings.NightEnd = $"{t:hh\\:mm}"; Raise(); Touch(); } } }

    public string Latitude
    {
        get => Settings.Latitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        set { Settings.Latitude = ParseCoord(value, 90); Raise(); Touch(); }
    }

    public string Longitude
    {
        get => Settings.Longitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        set { Settings.Longitude = ParseCoord(value, 180); Raise(); Touch(); }
    }

    private static double? ParseCoord(string s, double limit) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && Math.Abs(v) <= limit ? v : null;

    public MovementType Movement
    {
        get => Settings.Movement;
        set { if (Settings.Movement == value) return; Settings.Movement = value; Raise(); Raise(nameof(MovementHint)); Touch(); }
    }

    public string MovementHint => Loc.Get("movement." + Settings.Movement.ToString().ToLowerInvariant() + ".hint");

    public IReadOnlyList<Choice<NumeralFont>> NumeralChoices { get; } =
        Enum.GetValues<NumeralFont>().Select(f => new Choice<NumeralFont>(f, Loc.Get(NumeralFontInfo.NameKey(f)))).ToList();

    public NumeralFont Numerals
    {
        get => Settings.Numerals;
        set { if (Settings.Numerals != value) { Settings.Numerals = value; Raise(); Touch(); } }
    }

    public DialSize Size { get => Settings.Size; set { if (Settings.Size != value) { Settings.Size = value; Raise(); Touch(); } } }
    public BurnInLevel BurnIn { get => Settings.BurnIn; set { if (Settings.BurnIn != value) { Settings.BurnIn = value; Raise(); Touch(); } } }

    public MonitorMode MonitorMode
    {
        get => Settings.Monitors;
        set
        {
            if (Settings.Monitors == value) return;
            Settings.Monitors = value;
            // Seed the selection with everything the first time the user picks "Selected".
            if (value == MonitorMode.Selected && Settings.SelectedMonitors.Count == 0)
                foreach (var m in Monitors) Settings.SelectedMonitors.Add(m.DeviceName);
            foreach (var m in Monitors) m.RaiseEnabled();
            Raise(); Raise(nameof(ShowMonitorChecks));
            Touch();
        }
    }

    public bool ShowMonitorChecks => Settings.Monitors == MonitorMode.Selected;

    public IReadOnlyList<Choice<BackgroundMode>> BackgroundModeChoices { get; } = new[]
    {
        new Choice<BackgroundMode>(BackgroundMode.MatchDial, Loc.Get("display.background.match")),
        new Choice<BackgroundMode>(BackgroundMode.Solid, Loc.Get("display.background.custom")),
    };

    public BackgroundMode BackgroundMode
    {
        get => Settings.Background;
        set
        {
            if (Settings.Background == value) return;
            Settings.Background = value;
            Raise(); Raise(nameof(ShowCustomBackground));
            Touch();
        }
    }

    public bool ShowCustomBackground => Settings.Background == BackgroundMode.Solid;

    public string BackgroundHex
    {
        get => Settings.BackgroundHex;
        set { if (Rgba.TryFromHex(value, out var c)) { Settings.BackgroundHex = c.ToHex(); Raise(); Touch(); } else Raise(); }
    }

    public int FpsCap { get => Settings.FpsCap; set { if (Settings.FpsCap != value) { Settings.FpsCap = value; Raise(); Touch(); } } }
    public bool ReduceOnBattery { get => Settings.ReduceOnBattery; set { if (Settings.ReduceOnBattery != value) { Settings.ReduceOnBattery = value; Raise(); Touch(); } } }
    public int BatteryFpsCap { get => Settings.BatteryFpsCap; set { if (Settings.BatteryFpsCap != value) { Settings.BatteryFpsCap = value; Raise(); Touch(); } } }

    public string GpuAdapter
    {
        get => Settings.GpuAdapter ?? "";
        set { var v = string.IsNullOrEmpty(value) ? null : value; if (Settings.GpuAdapter != v) { Settings.GpuAdapter = v; Raise(); Touch(); } }
    }

    public bool TickSound { get => Settings.TickSound; set { if (Settings.TickSound != value) { Settings.TickSound = value; Raise(); Touch(); TickSoundPlayer.Preview(value); } } }

    public string Language
    {
        get => Settings.Language;
        set
        {
            if (Settings.Language == value || value == null) return;
            Settings.Language = value;
            Raise();
            Persist();
            _languageChanged();
        }
    }

    // ---------------------------------------------------------------- persistence

    /// <summary>Called by any change: preview updates immediately; disk write is debounced.</summary>
    public void Touch()
    {
        Preview.Apply(Settings.Clone());
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Persist()
    {
        try { _store.Save(Settings); }
        catch (Exception ex) { Logger.Error("Saving settings failed", ex); }
    }

    // ---------------------------------------------------------------- about / updates

    public string VersionText => Loc.Format("about.version", AppInfo.Version);

    private string _updateStatus = "";
    private string? _updateUrl;
    public string UpdateStatus { get => _updateStatus; private set { _updateStatus = value; Raise(); } }
    public bool UpdateAvailable => _updateUrl != null;

    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenUpdateCommand { get; }
    public ICommand SupportCommand { get; }
    public ICommand ResetOffsetCommand { get; }

    private async Task CheckForUpdatesAsync()
    {
        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();
        UpdateStatus = Loc.Get("about.update.checking");
        _updateUrl = null; Raise(nameof(UpdateAvailable));
        var r = await UpdateChecker.CheckAsync(Version.Parse(AppInfo.Version), ct: _updateCts.Token);
        switch (r.State)
        {
            case UpdateState.Available:
                _updateUrl = r.Info!.Url;
                UpdateStatus = Loc.Format("about.update.available", r.Info.Latest);
                break;
            case UpdateState.UpToDate: UpdateStatus = Loc.Get("about.update.latest"); break;
            default: UpdateStatus = Loc.Get("about.update.failed"); break;
        }
        Raise(nameof(UpdateAvailable));
        (OpenUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        Settings.LastUpdateCheckUtc = DateTime.UtcNow;
        Touch();
    }

    private static void Open(string? url)
    {
        if (url == null) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error("Could not open " + url, ex); }
    }
}



