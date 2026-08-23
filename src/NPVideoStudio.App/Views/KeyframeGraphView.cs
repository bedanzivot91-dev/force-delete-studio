using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.Views;

public sealed class KeyframeGraphPointEventArgs(double x, double y) : EventArgs
{ public double NormalizedX { get; } = x; public double NormalizedY { get; } = y; }

public sealed class KeyframeGraphView : Control
{
    public static readonly StyledProperty<IReadOnlyList<ClipKeyframe>?> PointsProperty = AvaloniaProperty.Register<KeyframeGraphView, IReadOnlyList<ClipKeyframe>?>(nameof(Points));
    public static readonly StyledProperty<double> DurationProperty = AvaloniaProperty.Register<KeyframeGraphView, double>(nameof(Duration), 1);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<KeyframeGraphView, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<KeyframeGraphView, double>(nameof(Maximum), 1);
    static KeyframeGraphView() => AffectsRender<KeyframeGraphView>(PointsProperty, DurationProperty, MinimumProperty, MaximumProperty);
    public IReadOnlyList<ClipKeyframe>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public event EventHandler<KeyframeGraphPointEventArgs>? PointRequested;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); var p=e.GetPosition(this);
        PointRequested?.Invoke(this,new KeyframeGraphPointEventArgs(Math.Clamp(p.X/Math.Max(1,Bounds.Width),0,1),Math.Clamp(p.Y/Math.Max(1,Bounds.Height),0,1)));
    }
    public override void Render(DrawingContext c)
    {
        base.Render(c); c.DrawRectangle(new SolidColorBrush(Color.FromArgb(80,20,24,34)),new Pen(new SolidColorBrush(Color.FromArgb(100,150,160,180))),new Rect(Bounds.Size));
        if(Points is not {Count:>0}) return; var ordered=Points.OrderBy(p=>p.TimeSeconds).ToArray(); var range=Math.Max(1e-9,Maximum-Minimum); var duration=Math.Max(1e-9,Duration);
        Point Map(ClipKeyframe p)=>new(Math.Clamp(p.TimeSeconds/duration,0,1)*Bounds.Width,(1-Math.Clamp((p.Value-Minimum)/range,0,1))*Bounds.Height);
        var pen=new Pen(Brushes.DeepSkyBlue,2); for(int i=1;i<ordered.Length;i++) c.DrawLine(pen,Map(ordered[i-1]),Map(ordered[i]));
        foreach(var p in ordered){var q=Map(p);c.DrawEllipse(Brushes.Gold,new Pen(Brushes.White,1),q,4,4);}
    }
}
