using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Murbingo.App;
// Shared appearance for the two pools and the last drawn ball.
/// <summary>Shares ball artwork and number rendering between both pools and the last-ball display.</summary>
internal static class BallDrawing
{
    /// <summary>Draws a shadow, custom sprite or fallback sphere, and a rotated number with the same visual style everywhere.</summary>
    public static void Draw(DrawingContext dc, int number, Rect bounds, double pixelsPerDip, ImageSource? sprite = null, double angle = 0, Brush? numberBrush = null)
    {
        double diameter = bounds.Width;
        var center = new Point(bounds.X + diameter / 2, bounds.Y + diameter / 2);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), null, new(center.X + 1.6, center.Y + 2), diameter / 2, diameter / 2);
        dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
        if (sprite is not null)
        {
            dc.PushClip(new EllipseGeometry(center, diameter / 2, diameter / 2));
            dc.DrawImage(sprite, bounds);
            dc.Pop();
        }
        else
        {
            var gradient = new RadialGradientBrush
            {
                Center = new(.35, .28),
                GradientOrigin = new(.28, .2),
                RadiusX = .8,
                RadiusY = .8
            };
            gradient.GradientStops.Add(new(Color.FromRgb(255, 237, 184), 0));
            gradient.GradientStops.Add(new(Color.FromRgb(195, 149, 75), .55));
            gradient.GradientStops.Add(new(Color.FromRgb(91, 54, 29), 1));
            dc.DrawEllipse(gradient, new Pen(new SolidColorBrush(Color.FromRgb(82, 52, 28)), .8), center, diameter / 2, diameter / 2);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(251, 241, 215)), null, center, diameter * .32, diameter * .32);
        }

        var text = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), Math.Max(8, diameter * .36), numberBrush ?? Brushes.Black, pixelsPerDip);
        dc.DrawText(text, new(center.X - text.Width / 2, center.Y - text.Height / 2));
        dc.Pop();
    }
}

/// <summary>Displays the most recently drawn number as a ball without owning any game state.</summary>
public sealed class LastBall : FrameworkElement
{
    public Brush NumberBrush { get; set; } = Brushes.Black;
    public int? Number { get; private set; }
    public ImageSource? Sprite { get; set; }

    /// <summary>Updates the displayed number and invalidates rendering so restart can also clear the ball.</summary>
    public void SetNumber(int? number)
    {
        Number = number;
        InvalidateVisual();
    }

    /// <summary>Centers a ball within the available viewport and leaves clipping and movement to its parent window.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Number is not int number)
            return;
        double diameter = Math.Max(0, Math.Min(ActualWidth, ActualHeight) - 8);
        if (diameter <= 0)
            return;
        BallDrawing.Draw(dc, number, new Rect((ActualWidth - diameter) / 2, (ActualHeight - diameter) / 2, diameter, diameter), VisualTreeHelper.GetDpi(this).PixelsPerDip, Sprite, numberBrush: NumberBrush);
    }
}
