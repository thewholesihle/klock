namespace Klock.Core.Themes;

/// <summary>NOAA-style sunrise/sunset. Returns local clock times, or null in polar day/night.</summary>
public static class SolarCalc
{
    public readonly record struct SunTimes(TimeSpan Sunrise, TimeSpan Sunset);

    private const double Zenith = 90.833; // official: includes refraction + solar disc

    public static SunTimes? Compute(DateTime localDate, double latitude, double longitude, double utcOffsetHours)
    {
        int n = localDate.DayOfYear;
        double gamma = 2 * Math.PI / 365.0 * (n - 1);
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                  - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                      - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                      - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        double lat = latitude * Math.PI / 180.0;
        double cosHa = Math.Cos(Zenith * Math.PI / 180.0) / (Math.Cos(lat) * Math.Cos(decl)) - Math.Tan(lat) * Math.Tan(decl);
        if (cosHa is > 1 or < -1) return null;

        double ha = Math.Acos(cosHa) * 180.0 / Math.PI;
        double sunriseMin = 720 - 4 * (longitude + ha) - eqTime + utcOffsetHours * 60;
        double sunsetMin = 720 - 4 * (longitude - ha) - eqTime + utcOffsetHours * 60;
        return new SunTimes(TimeSpan.FromMinutes(Wrap(sunriseMin)), TimeSpan.FromMinutes(Wrap(sunsetMin)));
    }

    private static double Wrap(double minutes)
    {
        double m = minutes % 1440;
        return m < 0 ? m + 1440 : m;
    }
}
