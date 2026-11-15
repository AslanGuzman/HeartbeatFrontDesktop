using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Período que muestran la gráfica y la lista del historial.</summary>
public enum HistoryRange
{
    /// <summary>Los últimos 7 días.</summary>
    Week,

    /// <summary>Los últimos 30 días.</summary>
    Month,

    /// <summary>Desde el primer check-up hasta hoy.</summary>
    Mission,
}

/// <summary>Un indicador que se puede elegir para la gráfica.</summary>
public sealed partial class MetricOptionViewModel(ChartMetric metric, string name) : ObservableObject
{
    public ChartMetric Metric { get; } = metric;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Historial de check-ups del astronauta con la sesión abierta (MR-13, MR-14) y sus tendencias (NR-02): una gráfica por
/// indicador con los tramos de color del contrato, y un selector de período (7 días, 30 días o toda la misión) que
/// filtra la gráfica y la lista.
/// </summary>
public sealed partial class HistoryViewModel : PageViewModel, IDisposable
{
    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IIndicatorInfoProvider _info;
    private readonly IndicatorBands _bands;
    private readonly TimeProvider _clock;

    public HistoryViewModel(
        IHealthRecordStore store,
        ISessionContext session,
        IIndicatorInfoProvider info,
        IndicatorBands? bands = null,
        TimeProvider? clock = null)
    {
        _store = store;
        _session = session;
        _info = info;
        _bands = bands ?? IndicatorBands.Parse(ReferenceRanges.Json);
        _clock = clock ?? TimeProvider.System;

        foreach (ChartMetric metric in _bands.Metrics)
        {
            Metrics.Add(new MetricOptionViewModel(metric, NameOf(metric)));
        }

        // Por defecto, la frecuencia cardíaca (la lectura más habitual) y toda la misión.
        SelectedMetric = Metrics.FirstOrDefault(m => m.Metric.Indicator == IndicatorType.HeartRate) ?? Metrics.FirstOrDefault();
        if (SelectedMetric is not null)
        {
            SelectedMetric.IsSelected = true;
        }

        Chart = TrendChartModel.Empty;
        ChartSummary = string.Empty;
        EmptyText = string.Empty;

        // Por defecto, toda la misión; al cambiar el período se calcula todo por primera vez.
        Range = HistoryRange.Mission;
        _store.Changed += OnStoreChanged;
    }

    public override string Title => "Historial";

    public ObservableCollection<CheckUpRowViewModel> CheckUps { get; } = [];

    public ObservableCollection<MetricOptionViewModel> Metrics { get; } = [];

    public bool HasItems => CheckUps.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeek), nameof(IsMonth), nameof(IsMission), nameof(RangeText))]
    public partial HistoryRange Range { get; set; }

    public bool IsWeek => Range == HistoryRange.Week;

    public bool IsMonth => Range == HistoryRange.Month;

    public bool IsMission => Range == HistoryRange.Mission;

    public string RangeText => Range switch
    {
        HistoryRange.Week => "últimos 7 días",
        HistoryRange.Month => "últimos 30 días",
        _ => "toda la misión",
    };

    public MetricOptionViewModel? SelectedMetric { get; private set; }

    [ObservableProperty]
    public partial TrendChartModel Chart { get; private set; }

    /// <summary>Por ejemplo: «Frecuencia cardíaca · 4 lecturas · últimos 7 días».</summary>
    [ObservableProperty]
    public partial string ChartSummary { get; private set; }

    /// <summary>Lo que se dice cuando la lista está vacía: sin check-ups, o ninguno en el período.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; private set; }

    public void Dispose() => _store.Changed -= OnStoreChanged;

    partial void OnRangeChanged(HistoryRange value) => Reload();

    [RelayCommand]
    private void SelectRange(HistoryRange range) => Range = range;

    [RelayCommand]
    private void SelectMetric(MetricOptionViewModel? option)
    {
        if (option is null || option == SelectedMetric)
        {
            return;
        }

        foreach (MetricOptionViewModel metric in Metrics)
        {
            metric.IsSelected = metric == option;
        }

        SelectedMetric = option;
        Reload();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        CheckUps.Clear();
        IReadOnlyList<CheckUpRecord> all = _session.CurrentUser is { } user ? _store.GetCheckUps(user.Id) : [];
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset from = StartOf(all, now);

        // Toda la misión incluye el último check-up aunque el reloj del equipo vaya por detrás de él.
        DateTimeOffset to = Range == HistoryRange.Mission && all.Count > 0 ? all.Max(c => c.RecordedAt) > now ? all.Max(c => c.RecordedAt) : now : now;
        List<CheckUpRecord> inRange = all.Where(c => c.RecordedAt >= from && c.RecordedAt <= to).ToList();

        foreach (CheckUpRecord checkUp in inRange)
        {
            CheckUps.Add(ToRow(checkUp));
        }

        EmptyText = all.Count == 0
            ? "Aún no hay check-ups. Registra el primero desde la sección Check-up."
            : "No hay check-ups en este período.";

        if (SelectedMetric is { } selected)
        {
            Chart = TrendChartBuilder.Build(selected.Metric, selected.Name, inRange, from, to);
            string count = Chart.Points.Count == 1 ? "1 lectura" : $"{Chart.Points.Count} lecturas";
            ChartSummary = $"{selected.Name} · {count} · {RangeText}";
        }

        OnPropertyChanged(nameof(HasItems));
    }

    /// <summary>Inicio del período: 7 o 30 días atrás, o el primer check-up de la misión.</summary>
    private DateTimeOffset StartOf(IReadOnlyList<CheckUpRecord> all, DateTimeOffset now) => Range switch
    {
        HistoryRange.Week => now.AddDays(-7),
        HistoryRange.Month => now.AddDays(-30),
        _ => all.Count == 0 ? now.AddDays(-7) : all.Min(c => c.RecordedAt),
    };

    private string NameOf(ChartMetric metric)
    {
        string name = _info.Find(IndicatorPresentation.IdOf(metric.Indicator))?.Name ?? metric.Indicator.ToString();
        return metric.Indicator == IndicatorType.BloodPressure
            ? name + (metric.IsSecondary ? " · diastólica" : " · sistólica")
            : name;
    }

    private CheckUpRowViewModel ToRow(CheckUpRecord checkUp) => new(
        checkUp.RecordedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        checkUp.OverallStatus,
        StatusPalette.Name(checkUp.OverallStatus),
        checkUp.IsRejected ? $"Rechazado: {checkUp.SyncError}" : checkUp.IsSynced ? "Sincronizado" : "Pendiente de sincronizar",
        checkUp.IsSynced,
        checkUp.Readings
            .Select(r => new ReadingRowViewModel(
                _info.Find(IndicatorPresentation.IdOf(r.Input.Indicator))?.Name ?? r.Input.Indicator.ToString(),
                IndicatorPresentation.FormatValue(r.Input),
                r.Evaluation.Status,
                StatusPalette.Name(r.Evaluation.Status),
                IndicatorPresentation.GuidanceOf(r.Evaluation)))
            .ToList());
}
