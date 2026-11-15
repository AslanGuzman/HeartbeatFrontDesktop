using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Un tramo sombreado de la gráfica, ya recortado al eje vertical.</summary>
public sealed record ChartBand(HealthStatus Status, double From, double To);

/// <summary>Una lectura dibujada: cuándo, cuánto y en qué estado estaba.</summary>
/// <param name="FromWatch">La lectura viene del reloj (<c>source = wearable</c>): se dibuja con otro marcador.</param>
public sealed record ChartPoint(DateTimeOffset At, double Value, HealthStatus Status, bool FromWatch = false);

/// <summary>
/// Todo lo que necesita dibujar la gráfica de un indicador (NR-02): tramos de color, puntos, ejes y marcas. Es un dato
/// inmutable en el espacio de valores; el control lo pasa a píxeles.
/// </summary>
public sealed record TrendChartModel(
    string Title,
    string Unit,
    int Decimals,
    IReadOnlyList<ChartBand> Bands,
    IReadOnlyList<ChartPoint> Points,
    double YMin,
    double YMax,
    IReadOnlyList<double> YTicks,
    DateTimeOffset XMin,
    DateTimeOffset XMax)
{
    public bool HasData => Points.Count > 0;

    /// <summary>Hay al menos un punto del reloj: la vista muestra la leyenda del marcador.</summary>
    public bool HasWatchPoints => Points.Any(p => p.FromWatch);

    /// <summary>Gráfica sin lecturas, con el eje del indicador y el período pedidos.</summary>
    public static TrendChartModel Empty { get; } = new(
        string.Empty, string.Empty, 0, [], [], 0, 1, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));
}

/// <summary>Construye la gráfica de una métrica a partir de los check-ups del astronauta.</summary>
public static class TrendChartBuilder
{
    private const double PaddingFraction = 0.15;

    /// <param name="title">Nombre de la métrica en pantalla.</param>
    /// <param name="from">Inicio del período (incluido).</param>
    /// <param name="to">Fin del período (incluido).</param>
    public static TrendChartModel Build(ChartMetric metric, string title, IEnumerable<CheckUpRecord> checkUps, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(checkUps);
        if (to <= from)
        {
            to = from.AddHours(1);
        }

        // Una lectura por check-up y métrica, del más viejo al más nuevo.
        List<ChartPoint> points = [];
        foreach (CheckUpRecord checkUp in checkUps.OrderBy(c => c.RecordedAt))
        {
            if (checkUp.RecordedAt < from || checkUp.RecordedAt > to)
            {
                continue;
            }

            ReadingRecord? reading = checkUp.Readings.FirstOrDefault(r => r.Input.Indicator == metric.Indicator);
            double? value = reading is null ? null : metric.ValueOf(reading.Input);
            if (value is { } v)
            {
                points.Add(new ChartPoint(checkUp.RecordedAt, v, metric.StatusOf(v), reading!.FromWatch));
            }
        }

        // Eje vertical: desde el primer hasta el último límite de banda, ampliado para que quepan los datos y con un margen.
        List<double> values = [];
        foreach (BandSpan band in metric.Bands)
        {
            if (band.From is { } f)
            {
                values.Add(f);
            }

            if (band.To is { } t)
            {
                values.Add(t);
            }
        }

        values.AddRange(points.Select(p => p.Value));
        double lo = values.Count == 0 ? metric.ValidMin : values.Min();
        double hi = values.Count == 0 ? metric.ValidMax : values.Max();
        double pad = hi - lo <= 0 ? 1 : (hi - lo) * PaddingFraction;
        double yMin = Math.Max(lo - pad, metric.ValidMin);
        double yMax = Math.Min(hi + pad, metric.ValidMax);
        if (yMax <= yMin)
        {
            yMin = lo - 1;
            yMax = hi + 1;
        }

        List<ChartBand> bands = [];
        foreach (BandSpan band in metric.Bands)
        {
            double start = Math.Max(band.From ?? yMin, yMin);
            double end = Math.Min(band.To ?? yMax, yMax);
            if (end > start)
            {
                bands.Add(new ChartBand(band.Status, start, end));
            }
        }

        return new TrendChartModel(
            title,
            IndicatorPresentation.UnitOf(metric.Indicator),
            metric.Indicator is IndicatorType.BodyTemperature or IndicatorType.Sleep ? 1 : 0,
            bands,
            points,
            yMin,
            yMax,
            NiceTicks(yMin, yMax, 5),
            from,
            to);
    }

    /// <summary>Marcas «redondas» (1, 2, 2,5, 5 × 10^n) entre los extremos, unas <paramref name="target"/>.</summary>
    public static IReadOnlyList<double> NiceTicks(double min, double max, int target)
    {
        double rough = (max - min) / Math.Max(1, target);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double step = new[] { 1d, 2d, 2.5d, 5d, 10d }
            .Select(m => m * magnitude)
            .First(s => s >= rough * 0.999);

        List<double> ticks = [];
        for (double tick = Math.Ceiling(min / step) * step; tick <= max + step * 1e-9; tick += step)
        {
            ticks.Add(Math.Round(tick, 6));
        }

        return ticks;
    }
}
