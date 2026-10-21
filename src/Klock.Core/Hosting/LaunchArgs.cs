namespace Klock.Core.Hosting;

public enum LaunchMode
{
    /// <summary>/s: full-screen on every monitor.</summary>
    Screensaver,
    /// <summary>/c (or no arguments): settings window.</summary>
    Configure,
    /// <summary>/p HWND: live preview inside the Screen Saver Settings dialog.</summary>
    PreviewPane,
    /// <summary>--preview: windowed/fullscreen demo clock, Esc to exit.</summary>
    Demo,
    /// <summary>--capture: render a still PNG.</summary>
    Capture,
    /// <summary>--sheet: contact sheet of all dials.</summary>
    Sheet,
    /// <summary>--icon: write the application icon.</summary>
    Icon,
    /// <summary>--bench: run a window for a few seconds and report frame timing.</summary>
    Bench,
    /// <summary>/a (change password) and anything else we deliberately ignore.</summary>
    Ignore,
}

public sealed record LaunchArgs(LaunchMode Mode, long Hwnd = 0, string[]? Rest = null)
{
    public string[] Options => Rest ?? Array.Empty<string>();
    public bool Has(string flag) => Options.Any(o => o.Equals(flag, StringComparison.OrdinalIgnoreCase));

    public string? Value(string name)
    {
        int i = Array.FindIndex(Options, o => o.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < Options.Length ? Options[i + 1] : null;
    }

    /// <summary>Parses the Windows screensaver protocol (<c>/s</c>, <c>/c:123</c>, <c>/p 123</c>, any case, / or -) plus our dev switches.</summary>
    public static LaunchArgs Parse(string[] args)
    {
        if (args.Length == 0) return new LaunchArgs(LaunchMode.Configure);

        string first = args[0].Trim();
        var rest = args.Skip(1).ToArray();

        // Dev switches use a double dash.
        if (first.StartsWith("--", StringComparison.Ordinal))
        {
            return first.Substring(2).ToLowerInvariant() switch
            {
                "preview" => new LaunchArgs(LaunchMode.Demo, 0, rest),
                "capture" => new LaunchArgs(LaunchMode.Capture, 0, rest),
                "sheet" => new LaunchArgs(LaunchMode.Sheet, 0, rest),
                "bench" => new LaunchArgs(LaunchMode.Bench, 0, rest),
                "icon" => new LaunchArgs(LaunchMode.Icon, 0, rest),
                "configure" => new LaunchArgs(LaunchMode.Configure, 0, rest),
                _ => new LaunchArgs(LaunchMode.Ignore, 0, rest),
            };
        }

        // Windows protocol: /x, -x, /x:HWND, /x HWND
        string token = first.TrimStart('/', '-');
        if (token.Length == 0) return new LaunchArgs(LaunchMode.Configure);

        char verb = char.ToLowerInvariant(token[0]);
        long hwnd = 0;
        string tail = token.Length > 1 ? token.Substring(1).TrimStart(':', ' ') : "";
        if (tail.Length > 0) long.TryParse(tail, out hwnd);
        else if (rest.Length > 0 && long.TryParse(rest[0], out var h2)) { hwnd = h2; rest = rest.Skip(1).ToArray(); }

        return verb switch
        {
            's' => new LaunchArgs(LaunchMode.Screensaver, 0, rest),
            'c' => new LaunchArgs(LaunchMode.Configure, hwnd, rest),
            'p' or 'l' => hwnd != 0 ? new LaunchArgs(LaunchMode.PreviewPane, hwnd, rest) : new LaunchArgs(LaunchMode.Ignore),
            _ => new LaunchArgs(LaunchMode.Ignore, 0, rest),
        };
    }
}

