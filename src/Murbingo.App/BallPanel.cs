using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Murbingo.Physics;

namespace Murbingo.App;
/// <summary>Renders a sorted pool or an animated upper pile while delegating physics to a UI-independent world.</summary>
public sealed class BallPanel : FrameworkElement
{
    public bool Chaotic { get; init; }
    public ImageSource? Sprite { get; set; }
    public Brush NumberBrush { get; set; } = Brushes.Black;
    public int Capacity { get; private set; }
    public IReadOnlyList<int> Numbers { get; private set; } = Array.Empty<int>();

    int[] slots = [];
    double[] angles = [];
    Point[] positions = [];
    Size layoutSize;
    int layoutSeed;
    BallWorld? physics;
    bool renderingSubscribed;
    TimeSpan? previousFrame;
    public List<Rect> LastBounds { get; } = new();

    /// <summary>Connects visibility and lifetime events so hidden or unloaded panels do not keep rendering callbacks alive.</summary>
    public BallPanel()
    {
        Loaded += (_, _) =>
        {
            ResizePhysics();
            RequestFrames();
        };
        Unloaded += (_, _) => StopFrames();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                ResizePhysics();
                RequestFrames();
            }
            else
                StopFrames();
        };
    }

    /// <summary>Chooses physical or static rendering and rebuilds only the visual world, preserving the game-owned number list.</summary>
    public void ConfigurePhysics(BallPhysicsSettings? settings)
    {
        StopFrames();
        var normalized = (settings ?? new()).Normalize();
        physics = Chaotic && normalized.Enabled ? new BallWorld(normalized) : null;
        if (physics != null)
        {
            physics.Reset(Capacity);
            ResizePhysics();
            physics.Synchronize(Numbers);
            RequestFrames();
        }

        InvalidateVisual();
    }

    /// <summary>Resizes the physics boundaries and redraws the pool when WPF changes its available area.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ResizePhysics();
        RequestFrames();
        InvalidateVisual();
    }

    /// <summary>Passes the inner panel dimensions to physics while reserving margins for complete ball silhouettes.</summary>
    void ResizePhysics() => physics?.Resize(Math.Max(0, ActualWidth - 16), Math.Max(0, ActualHeight - 16));
    /// <summary>Subscribes to frame updates only while a visible physical world needs motion or spawning.</summary>
    void RequestFrames()
    {
        if (renderingSubscribed || !IsLoaded || !IsVisible || physics?.NeedsUpdates != true)
            return;
        previousFrame = null;
        CompositionTarget.Rendering += AdvancePhysics;
        renderingSubscribed = true;
    }

    /// <summary>Unsubscribes the shared rendering event to avoid background calculations and leaked panel references.</summary>
    void StopFrames()
    {
        if (renderingSubscribed)
            CompositionTarget.Rendering -= AdvancePhysics;
        renderingSubscribed = false;
        previousFrame = null;
    }

    /// <summary>Converts render timestamps into elapsed time, advances the fixed-step world, and stops after the pile sleeps.</summary>
    void AdvancePhysics(object? sender, EventArgs e)
    {
        if (physics is null || e is not RenderingEventArgs frame)
        {
            StopFrames();
            return;
        }

        if (previousFrame is TimeSpan previous)
            physics.Advance((frame.RenderingTime - previous).TotalSeconds);
        previousFrame = frame.RenderingTime;
        InvalidateVisual();
        if (!physics.NeedsUpdates)
            StopFrames();
    }

    /// <summary>Starts a new visual round with fresh layout, rotations, bodies and spawn queue for the selected capacity.</summary>
    public void Reset(int count)
    {
        Capacity = count;
        slots = Enumerable.Range(0, count).ToArray();
        Random.Shared.Shuffle(slots);
        layoutSeed = Random.Shared.Next();
        positions = [];
        angles = Enumerable.Range(0, count).Select(_ => Random.Shared.NextDouble() * 32 - 16).ToArray();
        Numbers = Enumerable.Range(1, count).ToArray();
        previousFrame = null;
        if (physics != null)
        {
            physics.Reset(count);
            ResizePhysics();
            RequestFrames();
        }

        InvalidateVisual();
    }

    /// <summary>Synchronizes visible and queued balls with the authoritative remaining or drawn numbers from the game.</summary>
    public void SetNumbers(IReadOnlyList<int> numbers)
    {
        Numbers = numbers;
        physics?.Synchronize(numbers);
        RequestFrames();
        InvalidateVisual();
    }

    /// <summary>Draws physical bodies or stable static slots; the lower pool always uses ascending rows.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        LastBounds.Clear();
        if (Capacity == 0 || ActualWidth < 1 || ActualHeight < 1)
            return;
        if (physics != null)
        {
            double radius = physics.Radius * BallWorld.PixelsPerMetre;
            foreach (var ball in physics.Bodies)
            {
                var bounds = new Rect(8 + ball.X * BallWorld.PixelsPerMetre - radius, 8 + ball.Y * BallWorld.PixelsPerMetre - radius, radius * 2, radius * 2);
                LastBounds.Add(bounds);
                BallDrawing.Draw(dc, ball.Number, bounds, VisualTreeHelper.GetDpi(this).PixelsPerDip, Sprite, ball.Angle * 180 / Math.PI, NumberBrush);
            }

            return;
        }

        double width = Math.Max(1, ActualWidth - 16), height = Math.Max(1, ActualHeight - 16);
        double best = 0;
        int columns = 1;
        for (int c = 1; c <= Capacity; c++)
        {
            double candidate = Math.Min(width / c, height / Math.Ceiling((double)Capacity / c));
            if (candidate > best)
            {
                best = candidate;
                columns = c;
            }
        }

        double cell = Math.Min(best, 68), diameter = cell * (Chaotic ? .82 : .9);
        int rows = (int)Math.Ceiling((double)Capacity / columns);
        double x0 = 8 + (width - columns * cell) / 2;
        double y0 = 8 + (height - rows * cell) / 2;
        if (Chaotic && (positions.Length != Capacity || layoutSize != RenderSize))
        {
            positions = Enumerable.Range(0, Capacity).Select(slot => new Point(x0 + slot % columns * cell + (cell - diameter) / 2, y0 + slot / columns * cell + (cell - diameter) / 2)).ToArray();
            var random = new Random(layoutSeed);
            // Relax a packed grid into an irregular pile; reserve the missing balls' spaces.
            // The layout depends on the round and panel size, never on the drawn subset.
            for (int attempt = 0; attempt < Capacity * 40; attempt++)
            {
                int index = random.Next(Capacity);
                var previous = positions[index];
                var candidate = new Point(Math.Clamp(previous.X + (random.NextDouble() - .5) * cell * 1.8, 8, Math.Max(8, ActualWidth - diameter - 8)), Math.Clamp(previous.Y + (random.NextDouble() - .5) * cell * 1.8, 8, Math.Max(8, ActualHeight - diameter - 8)));
                if (!positions.Where((_, j) => j != index).Any(p => (p - candidate).Length < diameter + 1))
                    positions[index] = candidate;
            }

            layoutSize = RenderSize;
        }

        for (int i = 0; i < Numbers.Count; i++)
        {
            int number = Numbers[i];
            int slot = Chaotic ? slots[number - 1] : i;
            double x = Chaotic ? positions[slot].X : x0 + slot % columns * cell + (cell - diameter) / 2;
            double y = Chaotic ? positions[slot].Y : y0 + slot / columns * cell + (cell - diameter) / 2;
            LastBounds.Add(new(x, y, diameter, diameter));
            BallDrawing.Draw(dc, number, new Rect(x, y, diameter, diameter), VisualTreeHelper.GetDpi(this).PixelsPerDip, Sprite, Chaotic ? angles[number - 1] : 0, NumberBrush);
        }
    }
}
