using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Un indicador de visión con su último valor, estado, tendencia y gráfica.</summary>
public sealed record VisionIndicatorViewModel(
    string Id,
    string Name,
    bool HasData,
    string ValueText,
    string UnitText,
    HealthStatus? Status,
    string StatusText,
    string TrendText,
    string CorrelationText,
    string ChartTitle,
    TrendChartModel Chart);

/// <summary>
/// Visión y SANS (HB-FE-04): el astronauta registra su presión ocular (IOP) y su agudeza visual, y ve la tendencia de
/// cada una en una gráfica con las bandas del catálogo, más una nota corta sobre SANS y su relación con el CO₂, con
/// fuente. Tendencia y correlación las calcula <see cref="VisionAnalysis"/> (Shared), así que sale lo mismo sin
/// conexión que en el servidor. Todo es simulado, heurístico y no es un diagnóstico.
/// </summary>
public sealed partial class VisionViewModel : PageViewModel
{
    /// <summary>Días que abarcan la gráfica y el análisis.</summary>
    public const int PeriodDays = 30;

    // Límites de plausibilidad al teclear (evitan errores de captura); no son umbrales de estado: esos vienen del catálogo.
    internal const decimal MaxIopInput = 80m;
    internal const decimal MinAcuityInput = -0.5m;
    internal const decimal MaxAcuityInput = 3m;

    private readonly IVisionStore _store;
    private readonly ISessionContext _session;
    private readonly IUrlLauncher _launcher;
    private readonly TimeProvider _clock;

    public VisionViewModel(IVisionStore store, ISessionContext session, IUrlLauncher launcher, TimeProvider clock)
    {
        _store = store;
        _session = session;
        _launcher = launcher;
        _clock = clock;
        Indicators = [];
        Reload();
    }

    public override string Title => Loc.T("nav.vision");

    [ObservableProperty]
    public partial string IopText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AcuityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Confirmation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<VisionIndicatorViewModel> Indicators { get; set; }

    [ObservableProperty]
    public partial HealthStatus? OverallStatus { get; set; }

    [ObservableProperty]
    public partial string OverallText { get; set; } = string.Empty;

    /// <summary>Nota corta sobre SANS y su relación con el CO₂.</summary>
    public string SansNote => Loc.T("vision.sans.note");

    public string EvidenceText => Loc.T(
        "vision.evidence",
        VisionAnalysis.CatalogStatus == CatalogStatus.Verified ? Loc.T("vision.evidence.verified") : Loc.T("vision.evidence.heuristic"));

    public SourceLink Source { get; } = IndicatorsCatalog.SourceFor(IndicatorsCatalog.Find(VisionAnalysis.IopId)!);

    public string SourceTitle => Loc.T("simple.source", Source.Title);

    [RelayCommand]
    private void Add()
    {
        Confirmation = string.Empty;
        if (_session.CurrentUser is not { } user)
        {
            return;
        }

        bool hasIop = !string.IsNullOrWhiteSpace(IopText);
        bool hasAcuity = !string.IsNullOrWhiteSpace(AcuityText);
        if (!hasIop && !hasAcuity)
        {
            Error = Loc.T("vision.err.empty");
            return;
        }

        decimal iop = 0;
        decimal acuity = 0;
        if (hasIop && (!TryParse(IopText, out iop) || iop <= 0 || iop > MaxIopInput))
        {
            Error = Loc.T("vision.err.iop", MaxIopInput);
            return;
        }

        if (hasAcuity && (!TryParse(AcuityText, out acuity) || acuity < MinAcuityInput || acuity > MaxAcuityInput))
        {
            Error = Loc.T("vision.err.acuity", MinAcuityInput.ToString("0.0", Loc.Culture), MaxAcuityInput.ToString("0.0", Loc.Culture));
            return;
        }

        if (hasIop)
        {
            _store.Add(user.Id, VisionAnalysis.IopId, iop);
        }

        if (hasAcuity)
        {
            _store.Add(user.Id, VisionAnalysis.VisualAcuityId, acuity);
        }

        IopText = string.Empty;
        AcuityText = string.Empty;
        Error = string.Empty;
        Confirmation = Loc.T("vision.saved");
        Reload();
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (!_launcher.TryOpen(Source.Url))
        {
            Error = Loc.T("rad.err.link");
        }
    }

    // Acepta punto o coma decimal, sin separador de miles.
    internal static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    private void Reload()
    {
        DateTimeOffset to = _clock.GetUtcNow();
        DateTimeOffset from = to.AddDays(-PeriodDays);
        IReadOnlyList<EnvironmentReadingRecord> readings = _session.CurrentUser is { } user ? _store.GetReadings(user.Id, from) : [];

        List<EnvironmentReadingStatus> Statuses(string id) => readings
            .Where(r => string.Equals(r.IndicatorId, id, StringComparison.OrdinalIgnoreCase))
            .Select(EnvironmentStatusFactory.From)
            .OfType<EnvironmentReadingStatus>()
            .ToList();

        List<EnvironmentReadingStatus> iop = Statuses(VisionAnalysis.IopId);
        List<EnvironmentReadingStatus> acuity = Statuses(VisionAnalysis.VisualAcuityId);
        List<VisionPoint> co2 = readings
            .Where(r => string.Equals(r.IndicatorId, VisionAnalysis.Co2Id, StringComparison.OrdinalIgnoreCase))
            .Select(r => new VisionPoint(r.RecordedAt, r.Value))
            .ToList();

        VisionSummaryDto summary = VisionAnalysis.Analyze(iop, acuity, co2, from, to);
        Indicators =
        [
            Build(summary.Iop, summary.IopVsCo2, 1, from, to),
            Build(summary.VisualAcuity, summary.VisualAcuityVsCo2, 2, from, to),
        ];

        bool any = Indicators.Any(i => i.HasData);
        OverallStatus = any ? summary.Status : null;
        OverallText = OverallStatus is { } status
            ? Loc.T("status.overall", StatusPalette.Name(status))
            : Loc.T("vision.noData");
    }

    private static VisionIndicatorViewModel Build(IndicatorTrend trend, Co2Correlation correlation, int decimals, DateTimeOffset from, DateTimeOffset to)
    {
        string name = Loc.T(trend.IndicatorId == VisionAnalysis.IopId ? "vision.iop" : "vision.acuity");
        bool hasData = trend.Count > 0;
        string format = decimals == 1 ? "0.0" : "0.00";

        return new VisionIndicatorViewModel(
            trend.IndicatorId,
            name,
            hasData,
            trend.Latest is { } latest ? latest.ToString(format, Loc.Culture) : "—",
            trend.Unit,
            hasData ? trend.Status : null,
            StatusPalette.Name(hasData ? trend.Status : null),
            TrendText(trend, format),
            CorrelationText(correlation),
            Loc.T("vision.chartTitle", name),
            VisionChartBuilder.Build(trend, name, decimals, from, to));
    }

    private static string TrendText(IndicatorTrend trend, string format)
    {
        if (trend.Count == 0)
        {
            return Loc.T("vision.trend.none");
        }

        if (trend.Direction == TrendDirection.Unknown || trend.Change is not { } change)
        {
            return Loc.T("vision.trend.few");
        }

        string direction = trend.Direction switch
        {
            TrendDirection.Rising => Loc.T("vision.trend.rising"),
            TrendDirection.Falling => Loc.T("vision.trend.falling"),
            _ => Loc.T("vision.trend.stable"),
        };
        return Loc.T("vision.trend", direction, (change >= 0 ? "+" : string.Empty) + change.ToString(format, Loc.Culture), trend.Unit, PeriodDays);
    }

    private static string CorrelationText(Co2Correlation correlation)
    {
        if (correlation.R is not { } r)
        {
            return Loc.T("vision.corr.few", correlation.Pairs);
        }

        string strength = correlation.Strength switch
        {
            CorrelationStrength.Strong => Loc.T("vision.corr.strong"),
            CorrelationStrength.Moderate => Loc.T("vision.corr.moderate"),
            _ => Loc.T("vision.corr.weak"),
        };
        return Loc.T("vision.corr", strength, r.ToString("0.00", Loc.Culture), correlation.Pairs);
    }
}

/// <summary>Arma la gráfica de un indicador de visión con las bandas del catálogo (nunca con umbrales propios).</summary>
internal static class VisionChartBuilder
{
    private const double PaddingFraction = 0.15;

    public static TrendChartModel Build(IndicatorTrend trend, string title, int decimals, DateTimeOffset from, DateTimeOffset to)
    {
        IndicatorCatalogEntry entry = IndicatorsCatalog.Find(trend.IndicatorId)!;
        List<ChartPoint> points = trend.Readings
            .OrderBy(r => r.RecordedAt)
            .Select(r => new ChartPoint(r.RecordedAt, (double)r.Value, r.Status))
            .ToList();

        List<double> values = [];
        foreach (IndicatorBand band in entry.Bands)
        {
            if (band.Min is { } min)
            {
                values.Add((double)min);
            }

            if (band.Max is { } max)
            {
                values.Add((double)max);
            }
        }

        values.AddRange(points.Select(p => p.Value));
        double lo = values.Min();
        double hi = values.Max();
        double pad = hi - lo <= 0 ? 1 : (hi - lo) * PaddingFraction;
        double yMin = lo - pad;
        double yMax = hi + pad;

        List<ChartBand> bands = [];
        foreach (IndicatorBand band in entry.Bands)
        {
            double start = Math.Max(band.Min is { } mn ? (double)mn : yMin, yMin);
            double end = Math.Min(band.Max is { } mx ? (double)mx : yMax, yMax);
            if (end > start)
            {
                bands.Add(new ChartBand(band.Status, start, end));
            }
        }

        return new TrendChartModel(title, entry.Unit, decimals, bands, points, yMin, yMax, TrendChartBuilder.NiceTicks(yMin, yMax, 5), from, to);
    }
}
