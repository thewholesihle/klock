namespace Klock.Core.Hosting;

/// <summary>
/// Decides when user input should end the screensaver. The first moments after launch are ignored
/// (the cursor is often already over the new window and the shell emits stray mouse moves), and mouse
/// movement only counts once it travels beyond a small threshold from where it was first seen.
/// </summary>
public sealed class ExitInputDetector
{
    public double GraceSeconds { get; }
    public int MovementThresholdPx { get; }

    private (int X, int Y)? _anchor;

    public ExitInputDetector(double graceSeconds = 0.5, int movementThresholdPx = 10)
    {
        GraceSeconds = graceSeconds;
        MovementThresholdPx = movementThresholdPx;
    }

    /// <summary>A key press, mouse button or wheel event.</summary>
    public bool OnDiscreteInput(double secondsSinceStart) => secondsSinceStart >= GraceSeconds;

    /// <param name="x">Cursor X in screen coordinates.</param>
    /// <param name="y">Cursor Y in screen coordinates.</param>
    public bool OnMouseMove(int x, int y, double secondsSinceStart)
    {
        if (secondsSinceStart < GraceSeconds)
        {
            _anchor = (x, y); // keep re-anchoring while grace lasts
            return false;
        }
        if (_anchor is not { } a)
        {
            _anchor = (x, y);
            return false;
        }
        long dx = x - a.X, dy = y - a.Y;
        return dx * dx + dy * dy > (long)MovementThresholdPx * MovementThresholdPx;
    }
}
