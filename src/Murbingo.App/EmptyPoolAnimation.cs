using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Murbingo.App;
/// <summary>Loops a transparent dancing sprite only while the actual zero-ball viewport is visible.</summary>
public sealed class EmptyPoolAnimation : FrameworkElement, IDisposable
{
    BitmapSource[] frames = [];
    double fps;
    int frameIndex;
    TimeSpan? epoch;
    bool subscribed;
    bool disposed;
    /// <summary>Connects panel lifetime events so hidden easter eggs never keep a frame callback running.</summary>
    public EmptyPoolAnimation()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Start();
            else
                Stop();
        };
    }

    /// <summary>Loads and validates a local manifest and frozen atlas, leaving original video files outside the runtime.</summary>
    public async Task LoadAsync(string manifestPath)
    {
        string path = Config.Resolve(manifestPath);
        var definition = JsonSerializer.Deserialize<SpriteSheetDefinition>(await File.ReadAllTextAsync(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException();
        var sheet = await Sources.Image(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, definition.Sheet)));
        if (!definition.Fits(sheet.PixelWidth, sheet.PixelHeight))
            throw new InvalidDataException();
        if (disposed)
            return;
        frames = Enumerable.Range(0, definition.FrameCount).Select(i =>
        {
            var frame = new CroppedBitmap(sheet, new Int32Rect(i % definition.Columns * definition.FrameWidth, i / definition.Columns * definition.FrameHeight, definition.FrameWidth, definition.FrameHeight));
            frame.Freeze();
            return (BitmapSource)frame;
        }).ToArray();
        fps = definition.Fps;
        frameIndex = 0;
        epoch = null;
        InvalidateVisual();
        Start();
    }

    /// <summary>Subscribes only when a loaded animation is visible, preventing work during ordinary bingo rounds.</summary>
    void Start()
    {
        if (disposed || subscribed || !IsVisible || !IsLoaded || frames.Length == 0)
            return;
        epoch = null;
        frameIndex = 0;
        InvalidateVisual();
        CompositionTarget.Rendering += Advance;
        subscribed = true;
    }

    /// <summary>Releases the global rendering callback whenever the zero-ball view is hidden or unloaded.</summary>
    void Stop()
    {
        if (subscribed)
            CompositionTarget.Rendering -= Advance;
        subscribed = false;
        epoch = null;
        frameIndex = 0;
    }

    /// <summary>Selects a time-based frame without queuing timers or tying animation speed to monitor refresh rate.</summary>
    void Advance(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs rendering || frames.Length == 0)
            return;
        epoch ??= rendering.RenderingTime;
        int next = EmptyPoolAnimationState.FrameIndex((rendering.RenderingTime - epoch.Value).TotalSeconds, frames.Length, fps);
        if (next == frameIndex)
            return;
        frameIndex = next;
        InvalidateVisual();
    }

    /// <summary>Fits the transparent character inside the panel without stretching or covering the pool heading.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (frames.Length == 0 || ActualWidth <= 16 || ActualHeight <= 16)
            return;
        var frame = frames[frameIndex];
        double scale = Math.Min((ActualWidth - 16) / frame.PixelWidth, (ActualHeight - 16) / frame.PixelHeight);
        double width = frame.PixelWidth * scale, height = frame.PixelHeight * scale;
        dc.DrawImage(frame, new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height));
    }

    /// <summary>Stops rendering and releases atlas references when the host window closes.</summary>
    public void Dispose()
    {
        disposed = true;
        Stop();
        frames = [];
    }
}
