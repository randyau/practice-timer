using System.Windows;
using System.Windows.Media;
using PracticeTimer.Core.Detection;

namespace PracticeTimer.Views;

/// <summary>Level bar with a threshold marker and a noise-floor tick. Colour shows what the detector thinks it hears.</summary>
public sealed class LevelMeter : FrameworkElement
{
    private static DependencyProperty Prop(string name, object def) =>
        DependencyProperty.Register(name, def.GetType(), typeof(LevelMeter), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LevelProperty = Prop(nameof(Level), 0.0);
    public static readonly DependencyProperty ThresholdProperty = Prop(nameof(Threshold), 0.0);
    public static readonly DependencyProperty FloorProperty = Prop(nameof(Floor), 0.0);
    public static readonly DependencyProperty KindProperty = Prop(nameof(Kind), FrameTag.Quiet);

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double Threshold { get => (double)GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }
    public double Floor { get => (double)GetValue(FloorProperty); set => SetValue(FloorProperty, value); }
    public FrameTag Kind { get => (FrameTag)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRoundedRectangle((Brush)FindResource("Panel"), new Pen((Brush)FindResource("Border"), 1), new Rect(0.5, 0.5, w - 1, h - 1), 6, 6);
        var color = Kind switch
        {
            FrameTag.Active => Color.FromRgb(0x22, 0xA5, 0x5B),
            FrameTag.Metronome => Color.FromRgb(0x3B, 0x82, 0xF6),
            FrameTag.Loud => Color.FromRgb(0xE0, 0x9B, 0x1A),
            _ => Color.FromRgb(0x98, 0xA2, 0xB3),
        };
        dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(2, 2, Math.Max(0, (w - 4) * Level), h - 4), 4, 4);
        var floorX = 2 + (w - 4) * Floor;
        dc.DrawLine(new Pen((Brush)FindResource("Muted"), 1) { DashStyle = DashStyles.Dot }, new Point(floorX, 2), new Point(floorX, h - 2));
        var thrX = 2 + (w - 4) * Threshold;
        dc.DrawLine(new Pen(Brushes.Crimson, 2), new Point(thrX, 0), new Point(thrX, h));
    }
}
