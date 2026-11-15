using System.Windows;
using System.Windows.Media;

namespace Heartbeat.Desktop.Views;

/// <summary>La línea de la FC en vivo: las últimas lecturas, con las más recientes a la derecha.</summary>
public sealed class HeartRateLine : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(IReadOnlyList<int>),
        typeof(HeartRateLine),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxSamplesProperty = DependencyProperty.Register(
        nameof(MaxSamples),
        typeof(int),
        typeof(HeartRateLine),
        new FrameworkPropertyMetadata(300, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int>? Samples
    {
        get => (IReadOnlyList<int>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public int MaxSamples
    {
        get => (int)GetValue(MaxSamplesProperty);
        set => SetValue(MaxSamplesProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        IReadOnlyList<int>? samples = Samples;
        double width = ActualWidth;
        double height = ActualHeight;
        if (samples is not { Count: > 1 } || width <= 0 || height <= 0)
        {
            return;
        }

        int min = samples.Min() - 5;
        int max = samples.Max() + 5;
        double range = Math.Max(1, max - min);
        double step = width / Math.Max(1, MaxSamples - 1);
        double offset = width - ((samples.Count - 1) * step);

        StreamGeometry geometry = new();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(offset, height - ((samples[0] - min) / range * height)), false, false);
            ctx.PolyLineTo(
                samples.Skip(1).Select((bpm, i) => new Point(offset + ((i + 1) * step), height - ((bpm - min) / range * height))).ToArray(),
                true,
                true);
        }

        geometry.Freeze();
        Brush brush = (Brush)(TryFindResource("BrandRedBrush") ?? Brushes.Red);
        Pen pen = new(brush, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
