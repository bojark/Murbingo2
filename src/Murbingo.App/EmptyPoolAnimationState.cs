namespace Murbingo.App;
/// <summary>Defines when the zero-ball easter egg is active and calculates frames without UI dependencies.</summary>
public static class EmptyPoolAnimationState
{
    /// <summary>Uses the applied round count, so a pending zero or an exhausted normal round does not trigger the easter egg.</summary>
    public static bool ShouldShow(int roundBallCount) => roundBallCount == 0;
    /// <summary>Loops elapsed time through the atlas frames and safely handles invalid or not-yet-started clocks.</summary>
    public static int FrameIndex(double seconds, int frameCount, double fps)
    {
        if (frameCount <= 0 || !double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(fps) || fps <= 0 || !double.IsFinite(seconds * fps))
            return 0;
        return (int)(Math.Floor(seconds * fps) % frameCount);
    }
}

/// <summary>Describes a transparent frame atlas using portable dimensions rather than a runtime video codec.</summary>
public sealed record SpriteSheetDefinition
{
    public string Sheet { get; init; } = "";
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public int Columns { get; init; }
    public int FrameCount { get; init; }
    public double Fps { get; init; }

    /// <summary>Rejects invalid dimensions and out-of-bounds frame cells before WPF creates cropped images.</summary>
    public bool Fits(int width, int height) => !string.IsNullOrWhiteSpace(Sheet) && FrameWidth is> 0 and <= 1024 && FrameHeight is> 0 and <= 1024 && Columns is> 0 and <= 64 && FrameCount is> 0 and <= 600 && double.IsFinite(Fps) && Fps is> 0 and <= 60 && (long)Columns * FrameWidth <= width && ((FrameCount + Columns - 1L) / Columns) * FrameHeight <= height;
}
