using System.Windows;
using Klock.Core;
using Klock.Core.Hosting;
using Klock.Core.Settings;
using Microsoft.Win32;

namespace Klock.Settings;

public partial class App : Application
{
    private readonly SettingsStore _store = new();
    private KlockSettings _settings = new();
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Logger.Error("Settings UI crashed", ex.Exception);
            ex.Handled = true;
        };

        _settings = _store.Load();
        var cli = Environment.GetCommandLineArgs();
        int li = Array.IndexOf(cli, "--lang");
        if (li >= 0 && li + 1 < cli.Length) _settings.Language = cli[li + 1];
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(ApplyTheme);
        ShowWindow();
    }

    /// <summary>Picks Light/Dark from the Windows app mode, or the system-colour set in high contrast.</summary>
    public void ApplyTheme()
    {
        string name = SystemParameters.HighContrast ? "HighContrast"
            : SystemTheme.AppsUseLightTheme() == false ? "Dark" : "Light";
        Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"Themes/{name}.xaml", UriKind.Relative) };
        _window?.ApplyBackdrop(dark: name == "Dark");
    }

    private void ShowWindow()
    {
        Loc.SetLanguage(_settings.Language);
        _window = new MainWindow(new SettingsViewModel(_store, _settings, OnLanguageChanged));
        _window.Closed += (_, _) => { if (!_switching) Shutdown(); };
        _window.Show();
        ApplyTheme();
    }

    private bool _switching;

    /// <summary>Language changes take effect immediately by rebuilding the window.</summary>
    private void OnLanguageChanged()
    {
        _switching = true;
        var old = _window;
        _settings = _store.Load();
        ShowWindow();
        old?.Close();
        _switching = false;
    }
}

