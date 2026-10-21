using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Klock.Core;

namespace Klock.Settings;

public partial class MainWindow : Window
{
    private readonly SettingsViewModel _vm;
    private bool _dark;

    public MainWindow(SettingsViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        // Never open taller/wider than the usable screen (small laptops, high display scaling).
        var work = SystemParameters.WorkArea;
        Height = Math.Min(Height, work.Height * 0.94);
        Width = Math.Min(Width, work.Width * 0.94);
        MaxHeight = work.Height;
        FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag);

        // The very same renderer the screensaver uses, hosted in a child window.
        var host = new ClockPreviewHost(vm.Preview, () => vm.Settings.GpuAdapter);
        PreviewSlot.Child = host;
        // HwndHost ignores FlowDirection mirroring for its content; keep the preview unmirrored.
        PreviewSlot.FlowDirection = FlowDirection.LeftToRight;

        SourceInitialized += (_, _) => ApplyBackdrop(_dark);
        Loaded += async (_, _) =>
        {
            // QA/design review aid: `Klock.Settings.exe --scroll 900` opens already scrolled.
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--scroll");
            if (i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var off)) Scroller.ScrollToVerticalOffset(off);
            await LoadThumbnailsAsync();
        };
        Closing += (_, _) => vm.Persist();
        Closed += (_, _) => host.Dispose();
    }

    private async Task LoadThumbnailsAsync()
    {
        try
        {
            var thumbs = await Task.Run(DialThumbnails.Render);
            foreach (var d in _vm.Dials)
                if (thumbs.TryGetValue(d.Id, out var bmp)) d.Thumb = bmp;
        }
        catch (Exception ex)
        {
            Logger.Error("Rendering dial thumbnails failed", ex);
        }
    }

    /// <summary>Win11 Mica backdrop and matching dark title bar; plain window colour elsewhere.</summary>
    public void ApplyBackdrop(bool dark)
    {
        _dark = dark;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int useDark = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref useDark, sizeof(int));

        bool mica = Environment.OSVersion.Version.Build >= 22621 && !SystemParameters.HighContrast;
        if (mica)
        {
            int backdrop = 2; // DWMSBT_MAINWINDOW (Mica)
            if (DwmSetWindowAttribute(hwnd, 38 /* DWMWA_SYSTEMBACKDROP_TYPE */, ref backdrop, sizeof(int)) == 0)
            {
                Background = Brushes.Transparent;
                if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } ct }) ct.BackgroundColor = Colors.Transparent;
                return;
            }
        }
        SetResourceReference(BackgroundProperty, "WindowBg");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
