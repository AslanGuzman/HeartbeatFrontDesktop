using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Panel de salud: el estado actual de los seis indicadores en una sola pantalla (MR-05). Arriba, el estado general;
/// una tarjeta por indicador con su último valor, unidad, estado en color y texto (NR-01) y hora; y el botón para
/// registrar un nuevo check-up.
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly IIndicatorInfoPresenter _infoPresenter;
    private readonly INavigationService _navigation;

    public DashboardViewModel(
        IHealthRecordStore store,
        ISessionContext session,
        IIndicatorInfoProvider info,
        IIndicatorInfoPresenter infoPresenter,
        INavigationService navigation,
        TimeProvider clock)
    {
        _infoPresenter = infoPresenter;
        _navigation = navigation;

        SessionUser? user = session.CurrentUser;
        IReadOnlyList<CheckUpRecord> checkUps = user is null ? [] : store.GetCheckUps(user.Id);

        // Un check-up puede omitir indicadores (1 a 6 lecturas): cada tarjeta muestra la lectura más reciente de su indicador.
        List<IndicatorCardViewModel> cards = [];
        foreach (IndicatorType type in Enum.GetValues<IndicatorType>())
        {
            CheckUpRecord? source = checkUps.FirstOrDefault(c => c.Readings.Any(r => r.Input.Indicator == type));
            ReadingRecord? reading = source?.Readings.First(r => r.Input.Indicator == type);
            cards.Add(BuildCard(type, source, reading, info, clock));
        }

        Cards = cards;
        Greeting = user is null ? "Hola" : $"Hola, {user.FirstName}";
        DateText = clock.GetLocalNow().ToString("dddd, d 'de' MMMM", CultureInfo.CurrentCulture);

        List<IndicatorCardViewModel> withData = cards.Where(c => c.HasData).ToList();
        HasData = withData.Count > 0;
        OverallStatus = HasData ? withData.Max(c => c.Status) : null;
        OverallText = OverallStatus is { } overall
            ? $"Estado general: {StatusPalette.Name(overall)}"
            : "Sin mediciones todavía";

        int watch = withData.Count(c => c.Status == HealthStatus.Watch);
        int critical = withData.Count(c => c.Status == HealthStatus.Critical);
        SummaryText = BuildSummary(withData.Count, watch, critical, checkUps.FirstOrDefault(), clock);

        CriticalNames = string.Join(", ", withData.Where(c => c.Status == HealthStatus.Critical).Select(c => c.Name));
        HasCritical = critical > 0;
    }

    public override string Title => "Dashboard";

    public string Greeting { get; }

    public string DateText { get; }

    public bool HasData { get; }

    /// <summary>Peor estado entre las últimas lecturas de cada indicador; <c>null</c> si aún no hay ninguna.</summary>
    public HealthStatus? OverallStatus { get; }

    public string OverallText { get; }

    /// <summary>Qué requiere atención y cuándo fue el último check-up, por ejemplo «1 en vigilancia · Último check-up: Hoy, 19:45».</summary>
    public string SummaryText { get; }

    /// <summary>Hay al menos un indicador crítico: se destaca el aviso de contactar al oficial médico (MR-06).</summary>
    public bool HasCritical { get; }

    public string CriticalNames { get; }

    public IReadOnlyList<IndicatorCardViewModel> Cards { get; }

    /// <summary>Abre el formulario para registrar un nuevo check-up.</summary>
    [RelayCommand]
    private void NewCheckUp() => _navigation.NavigateTo<CheckUpViewModel>();

    /// <summary>Abre el panel de información; con un id lo deja directamente en ese indicador.</summary>
    [RelayCommand]
    private void ShowInfo(string? indicatorId) => _infoPresenter.Show(indicatorId);

    private static string BuildSummary(int measured, int watch, int critical, CheckUpRecord? latest, TimeProvider clock)
    {
        if (latest is null)
        {
            return "Registra tu primer check-up para ver aquí el estado de tus indicadores.";
        }

        List<string> parts = [];
        if (critical > 0)
        {
            parts.Add(critical == 1 ? "1 crítico" : $"{critical} críticos");
        }

        if (watch > 0)
        {
            parts.Add(watch == 1 ? "1 en vigilancia" : $"{watch} en vigilancia");
        }

        string attention = parts.Count > 0
            ? string.Join(", ", parts)
            : measured == 6 ? "Todos los indicadores en rango" : $"{measured} de 6 indicadores medidos, todos en rango";

        return $"{attention} · Último check-up: {IndicatorPresentation.FormatWhen(latest.RecordedAt, clock)}";
    }

    private static IndicatorCardViewModel BuildCard(IndicatorType type, CheckUpRecord? source, ReadingRecord? reading, IIndicatorInfoProvider info, TimeProvider clock)
    {
        string id = IndicatorPresentation.IdOf(type);
        string name = info.Find(id)?.Name ?? id;
        string glyph = IndicatorPresentation.GlyphOf(type);
        string unit = IndicatorPresentation.UnitOf(type);

        if (source is null || reading is null)
        {
            return new IndicatorCardViewModel(
                id, glyph, name, null, StatusPalette.Name(null), "—", unit, "Sin mediciones",
                "Registra un check-up para ver el estado de este indicador.", false);
        }

        return new IndicatorCardViewModel(
            id,
            glyph,
            name,
            reading.Evaluation.Status,
            StatusPalette.Name(reading.Evaluation.Status),
            IndicatorPresentation.FormatNumber(reading.Input),
            unit,
            IndicatorPresentation.FormatWhen(source.RecordedAt, clock),
            IndicatorPresentation.GuidanceOf(reading.Evaluation),
            true);
    }
}
