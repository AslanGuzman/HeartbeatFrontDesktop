using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.Views;

/// <summary>
/// Gráfica de tendencia de un indicador (NR-02): fondo sombreado con los tramos de color del contrato (el estado nunca
/// se indica solo con el color: cada tramo lleva su nombre al margen derecho), línea de lecturas y un punto por
/// lectura coloreado con su estado. Dibuja un <see cref="TrendChartModel"/>; al pasar el ratón por un punto muestra el
/// valor y la fecha.
/// </summary>
public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(TrendChartModel), typeof(TrendChart),
        new FrameworkPropertyMetadata(TrendChartModel.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnModelChanged));

    private const double Left = 54;
    private const double Right = 78;
    private const double Top = 10;
    private const double Bottom = 30;
    private const double HitRadius = 14;

    private static readonly Brush Grid = Frozen(Color.FromRgb(0xE1, 0xE3, 0xF0));
    private static readonly Brush Axis = Frozen(Color.FromRgb(0x6A, 0x6E, 0x86));
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x05, 0x59));

    public TrendChart()
    {
        SnapsToDevicePixels = true;
        MinHeight = 220;
    }

    public TrendChartModel Model
    {
        get => (TrendChartModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TrendChart)d).ToolTip = null;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 260 : availableSize.Height);

    private Rect Plot => new(Left, Top, Math.Max(10, ActualWidth - Left - Right), Math.Max(10, ActualHeight - Top - Bottom));

    protected override void OnRender(DrawingContext dc)
    {
        TrendChartModel model = Model;
        Rect plot = Plot;

        // Un fondo transparente hace que toda el área responda al ratón.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        double ScaleY(double value) => plot.Bottom - ((value - model.YMin) / (model.YMax - model.YMin) * plot.Height);

        // Tramos de color.
        foreach (ChartBand band in model.Bands)
        {
            double top = ScaleY(band.To);
            double bottom = ScaleY(band.From);
            dc.DrawRectangle(StatusPalette.Soft(band.Status), null, new Rect(plot.Left, top, plot.Width, Math.Max(0, bottom - top)));

            // El nombre del estado al margen derecho, para no depender solo del color.
            if (bottom - top >= 14)
            {
                DrawText(dc, StatusPalette.Name(band.Status), plot.Right + 8, (top + bottom) / 2 - 7, StatusPalette.Text(band.Status), 11, FontWeights.SemiBold);
            }
        }

        // Rejilla y etiquetas del eje vertical.
        Pen gridPen = new(Grid, 1);
        gridPen.Freeze();
        string format = model.Decimals == 0 ? "0" : "0." + new string('0', model.Decimals);
        foreach (double tick in model.YTicks)
        {
            double y = ScaleY(tick);
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            FormattedText label = Text(tick.ToString(format, CultureInfo.CurrentCulture), Axis, 11, FontWeights.Normal);
            dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }

        if (!string.IsNullOrEmpty(model.Unit))
        {
            DrawText(dc, model.Unit, 2, 0, Axis, 11, FontWeights.Normal);
        }

        // Eje horizontal: fechas repartidas por el período.
        double span = (model.XMax - model.XMin).TotalSeconds;
        bool showTime = span <= 3 * 24 * 3600;
        const int XTickCount = 5;
        for (int i = 0; i < XTickCount; i++)
        {
            DateTimeOffset at = model.XMin.AddSeconds(span * i / (XTickCount - 1));
            double x = plot.Left + plot.Width * i / (XTickCount - 1);
            string text = at.ToLocalTime().ToString(showTime ? "dd/MM HH:mm" : "dd/MM", CultureInfo.CurrentCulture);
            FormattedText label = Text(text, Axis, 11, FontWeights.Normal);
            double labelX = Math.Clamp(x - label.Width / 2, 0, Math.Max(0, ActualWidth - label.Width));
            dc.DrawText(label, new Point(labelX, plot.Bottom + 8));
        }

        if (!model.HasData)
        {
            FormattedText empty = Text(Loc.T("chart.empty"), Axis, 13, FontWeights.SemiBold);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            return;
        }

        // Línea y puntos.
        Point[] pixels = model.Points.Select(p => ToPixel(p, model, plot, ScaleY)).ToArray();
        if (pixels.Length > 1)
        {
            Pen linePen = new(Line, 2) { LineJoin = PenLineJoin.Round };
            linePen.Freeze();
            StreamGeometry geometry = new();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(pixels[0], false, false);
                ctx.PolyLineTo(pixels.Skip(1).ToArray(), true, true);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, linePen, geometry);
        }

        Pen ring = new(Brushes.White, 2);
        ring.Freeze();
        for (int i = 0; i < pixels.Length; i++)
        {
            if (model.Points[i].FromWatch)
            {
                // Lectura del reloj: rombo del color del estado, para distinguirla de las tecleadas (círculo).
                Pen outline = new(StatusPalette.Dot(model.Points[i].Status), 2);
                outline.Freeze();
                Point c = pixels[i];
                StreamGeometry diamond = new();
                using (StreamGeometryContext ctx = diamond.Open())
                {
                    ctx.BeginFigure(new Point(c.X, c.Y - 7), true, true);
                    ctx.LineTo(new Point(c.X + 7, c.Y), true, false);
                    ctx.LineTo(new Point(c.X, c.Y + 7), true, false);
                    ctx.LineTo(new Point(c.X - 7, c.Y), true, false);
                }

                diamond.Freeze();
                dc.DrawGeometry(Brushes.White, outline, diamond);
                dc.DrawEllipse(StatusPalette.Dot(model.Points[i].Status), null, c, 2.5, 2.5);
            }
            else
            {
                dc.DrawEllipse(StatusPalette.Dot(model.Points[i].Status), ring, pixels[i], 5, 5);
            }
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        TrendChartModel model = Model;
        if (!model.HasData)
        {
            return;
        }

        Rect plot = Plot;
        Point mouse = e.GetPosition(this);
        ChartPoint? nearest = null;
        double best = HitRadius;
        foreach (ChartPoint point in model.Points)
        {
            Point pixel = ToPixel(point, model, plot, v => plot.Bottom - ((v - model.YMin) / (model.YMax - model.YMin) * plot.Height));
            double distance = (pixel - mouse).Length;
            if (distance <= best)
            {
                best = distance;
                nearest = point;
            }
        }

        ToolTip = nearest is null ? null : TooltipOf(model, nearest);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ToolTip = null;
    }

    /// <summary>Texto del globo de un punto: valor con unidad, estado (con nombre, no solo color) y fecha.</summary>
    public static string TooltipOf(TrendChartModel model, ChartPoint point)
    {
        string format = model.Decimals == 0 ? "0" : "0." + new string('0', model.Decimals);
        string value = point.Value.ToString(format, CultureInfo.CurrentCulture);
        string text = $"{value} {model.Unit} · {StatusPalette.Name(point.Status)}{Environment.NewLine}{point.At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        return point.FromWatch ? text + Environment.NewLine + Loc.T("watch.readingFromWatch") : text;
    }

    private static Point ToPixel(ChartPoint point, TrendChartModel model, Rect plot, Func<double, double> scaleY)
    {
        double span = (model.XMax - model.XMin).TotalSeconds;
        double fraction = span <= 0 ? 0.5 : (point.At - model.XMin).TotalSeconds / span;
        return new Point(plot.Left + Math.Clamp(fraction, 0, 1) * plot.Width, scaleY(point.Value));
    }

    private FormattedText Text(string text, Brush brush, double size, FontWeight weight) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawText(DrawingContext dc, string text, double x, double y, Brush brush, double size, FontWeight weight) =>
        dc.DrawText(Text(text, brush, size, weight), new Point(x, y));

    private static Brush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
