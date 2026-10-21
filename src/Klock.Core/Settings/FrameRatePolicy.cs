namespace Klock.Core.Settings;

public static class FrameRatePolicy
{
    /// <summary>
    /// Frame cap to apply right now. 0 means "no software cap": present at the display's refresh rate (vsync).
    /// On battery the cap is reduced (default 30 fps) when the user has not opted out.
    /// </summary>
    public static int EffectiveCap(KlockSettings s, bool onBattery)
    {
        int cap = s.FpsCap;
        if (s.ReduceOnBattery && onBattery)
            cap = cap == 0 ? s.BatteryFpsCap : Math.Min(cap, s.BatteryFpsCap);
        return cap;
    }
}
