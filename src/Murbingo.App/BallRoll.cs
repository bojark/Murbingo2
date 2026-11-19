namespace Murbingo.App;
// Pure geometry so the entrance can be checked without opening a window.
/// <summary>Stores UI-independent entrance geometry so rolling starts outside the viewport and can be tested without WPF.</summary>
internal readonly record struct BallRoll(double StartX, double StartAngle)
{
    /// <summary>Calculates a hidden starting position and matching rotation from the ball radius and viewport width.</summary>
    public static BallRoll ForViewport(double width, double height)
    {
        double diameter = Math.Max(1, Math.Min(width, height) - 8);
        // Start completely beyond the clipped left edge, including the shadow.
        double distance = width / 2 + diameter / 2 + 8;
        return new(-distance, -distance / (diameter / 2) * 180 / Math.PI);
    }
}
