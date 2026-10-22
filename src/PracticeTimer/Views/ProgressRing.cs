using System.Windows;
using System.Windows.Media;

namespace PracticeTimer.Views;

/// <summary>Ring that fills clockwise with <see cref="Value"/> (0..1); shows a check mark when <see cref="Done"/>.</summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DoneProperty = DependencyProperty.Register(nameof(Done), typeof(bool), typeof(ProgressRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool Done { get => (bool)GetValue(DoneProperty); set => SetValue(DoneProperty, value); }

    private static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xA5, 0x5B)));
    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8) return;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = size / 2 - 3;
        var track = new Pen((Brush)FindResource("Border"), 3);
        dc.DrawEllipse(null, track, centre, r, r);
        if (Done)
        {
            var green = new Pen(Green, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawEllipse(null, green, centre, r, r);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(centre.X - r * 0.45, centre.Y), false, false);
                c.LineTo(new Point(centre.X - r * 0.1, centre.Y + r * 0.35), true, true);
                c.LineTo(new Point(centre.X + r * 0.5, centre.Y - r * 0.35), true, true);
            }
            dc.DrawGeometry(null, green, g);
            return;
        }
        var v = Math.Clamp(Value, 0, 1);
        if (v <= 0) return;
        var accent = new Pen((Brush)FindResource("Accent"), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 0.999) { dc.DrawEllipse(null, accent, centre, r, r); return; }
        var angle = v * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - r);
        var end = new Point(centre.X + r * Math.Sin(angle), centre.Y - r * Math.Cos(angle));
        var arc = new StreamGeometry();
        using (var c = arc.Open())
        {
            c.BeginFigure(start, false, false);
            c.ArcTo(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, accent, arc);
    }
}
