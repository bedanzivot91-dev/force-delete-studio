using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NPVideoStudio.App.Views;

public sealed class AudioWaveformView : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PeaksProperty =
        AvaloniaProperty.Register<AudioWaveformView, IReadOnlyList<double>?>(nameof(Peaks));

    static AudioWaveformView() => AffectsRender<AudioWaveformView>(PeaksProperty);

    public IReadOnlyList<double>? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Peaks is not { Count: > 0 } || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var brush = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255));
        var pen = new Pen(brush, Math.Max(1, Bounds.Width / Peaks.Count * 0.58));
        var center = Bounds.Height / 2;
        var step = Bounds.Width / Peaks.Count;
        for (var i = 0; i < Peaks.Count; i++)
        {
            var amplitude = Math.Clamp(Peaks[i], 0, 1) * (center - 1);
            var x = (i + 0.5) * step;
            context.DrawLine(pen, new Point(x, center - amplitude), new Point(x, center + amplitude));
        }
    }
}
