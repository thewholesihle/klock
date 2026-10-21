using System.Runtime.InteropServices;
using Klock.Core.Time;

namespace Klock.Renderer.Audio;

/// <summary>
/// Optional, off-by-default tick. The samples are synthesised here (a damped click plus a short noise
/// burst), so there is no third-party audio to license. Very quiet by design.
/// </summary>
public static class TickSoundPlayer
{
    private const int SampleRate = 44100;
    private static readonly byte[] Tick = Synthesize(2900, 0.20);
    private static readonly byte[] Tock = Synthesize(2300, 0.17);
    private static GCHandle _tickPin, _tockPin;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);
    private const uint SND_ASYNC = 1, SND_MEMORY = 4, SND_NODEFAULT = 2;

    public static void Play(bool alternate)
    {
        try
        {
            var buf = alternate ? Tock : Tick;
            ref var pin = ref (alternate ? ref _tockPin : ref _tickPin);
            if (!pin.IsAllocated) pin = GCHandle.Alloc(buf, GCHandleType.Pinned); // pinned for process lifetime: async playback reads it
            PlaySound(pin.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No audio stack (e.g. Server Core): silently skip.
        }
    }

    /// <summary>Plays one tick when the user switches the option on, so they can hear what they chose.</summary>
    public static void Preview(bool enabled)
    {
        if (enabled) Play(false);
    }

    /// <summary>WAV bytes for a short click at <paramref name="freq"/> Hz with peak amplitude <paramref name="peak"/> (0..1).</summary>
    public static byte[] Synthesize(double freq, double peak)
    {
        int n = (int)(SampleRate * 0.028);
        var pcm = new short[n];
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double click = Math.Sin(2 * Math.PI * freq * t) * Math.Exp(-t / 0.0045);
            double burst = (rng.NextDouble() * 2 - 1) * Math.Exp(-t / 0.0012) * 0.55;
            double fadeIn = Math.Min(1, i / 12.0); // avoid a hard onset pop
            pcm[i] = (short)(Math.Clamp((click * 0.8 + burst) * fadeIn * peak, -1, 1) * short.MaxValue);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        foreach (var s in pcm) w.Write(s);
        return ms.ToArray();
    }
}

/// <summary>Fires <see cref="TickSoundPlayer"/> on the clock's real tick boundaries (1 Hz Quartz, 4 Hz Mechanical).</summary>
public sealed class TickScheduler : IDisposable
{
    private readonly Func<double> _secondsOfDay;
    private readonly Func<MovementType> _movement;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _paused;
    private readonly Thread _thread;
    private volatile bool _stop;

    public TickScheduler(Func<double> secondsOfDay, Func<MovementType> movement, Func<bool> enabled, Func<bool> paused)
    {
        _secondsOfDay = secondsOfDay;
        _movement = movement;
        _enabled = enabled;
        _paused = paused;
        _thread = new Thread(Run) { IsBackground = true, Name = "Klock tick" };
        _thread.Start();
    }

    private void Run()
    {
        bool alt = false;
        while (!_stop)
        {
            var m = _movement();
            if (!_enabled() || _paused() || m == MovementType.Digital)
            {
                Thread.Sleep(250);
                continue;
            }

            double rate = m == MovementType.Mechanical ? MovementEngine.MechanicalBeatsPerSecond : 1.0;
            double now = _secondsOfDay();
            double next = Math.Floor(now * rate + 1e-9) + 1;
            double waitMs = (next / rate - now) * 1000.0;
            if (waitMs > 20) { Thread.Sleep((int)(waitMs - 10)); continue; } // coarse sleep, then re-evaluate
            if (waitMs > 1) Thread.Sleep(1);
            if (_secondsOfDay() * rate >= next - 1e-6 || waitMs <= 1)
            {
                TickSoundPlayer.Play(alt);
                alt = m == MovementType.Mechanical && !alt;
                Thread.Sleep(Math.Max(1, (int)(1000.0 / rate * 0.5))); // never double-fire the same beat
            }
        }
    }

    public void Dispose() => _stop = true;
}
