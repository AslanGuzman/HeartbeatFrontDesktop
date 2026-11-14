using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Panel de salud: el estado actual de los seis indicadores en una pantalla (MR-05), con su estado en color y texto
/// (NR-01), la guía (MR-04) y acceso a la información y fuentes de cada uno (MR-07, NR-03).
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly IIndicatorInfoPresenter _infoPresenter;

    public DashboardViewModel(
        IHealthRecordStore store,
        ISessionContext session,
        IIndicatorInfoProvider info,
        IIndicatorInfoPresenter infoPresenter,
        TimeProvider clock)
    {
        _infoPresenter = infoPresenter;

        SessionUser? user = session.CurrentUser;
        IReadOnlyList<CheckUpRecord> checkUps = user is null ? [] : store.GetCheckUps(user.Id);

        // Un check-up puede omitir indicadores (1 a 6 lecturas): cada tarjeta muestra la lectura más reciente de su indicador.
        List<IndicatorCardViewModel> cards = [];
        List<HealthStatus> latest = [];
        foreach (IndicatorType type in Enum.GetValues<IndicatorType>())
        {
            CheckUpRecord? source = checkUps.FirstOrDefault(c => c.Readings.Any(r => r.Input.Indicator == type));
            ReadingRecord? reading = source?.Readings.First(r => r.Input.Indicator == type);
            if (reading is not null)
            {
                latest.Add(reading.Evaluation.Status);
            }

            cards.Add(BuildCard(type, source, reading, info));
        }

        Greeting = user is null ? "Hola" : $"Hola, {user.FirstName}";
        DateText = clock.GetLocalNow().ToString("dddd, d 'de' MMMM", CultureInfo.CurrentCulture);
        HasData = latest.Count > 0;
        OverallStatus = HasData ? latest.Max() : null;
        OverallText = OverallStatus is { } overall
            ? $"Estado general: {StatusPalette.Name(overall)}"
            : "Sin mediciones todavía";
        Cards = cards;
    }

    public override string Title => "Dashboard";

    public string Greeting { get; }

    public string DateText { get; }

    public bool HasData { get; }

    /// <summary>Estado del último check-up; <c>null</c> si aún no hay ninguno.</summary>
    public HealthStatus? OverallStatus { get; }

    public string OverallText { get; }

    public IReadOnlyList<IndicatorCardViewModel> Cards { get; }

    /// <summary>Abre el panel de información; con un id lo deja directamente en ese indicador.</summary>
    [RelayCommand]
    private void ShowInfo(string? indicatorId) => _infoPresenter.Show(indicatorId);

    private static IndicatorCardViewModel BuildCard(IndicatorType type, CheckUpRecord? source, ReadingRecord? reading, IIndicatorInfoProvider info)
    {
        string id = IndicatorPresentation.IdOf(type);
        string name = info.Find(id)?.Name ?? id;
        string glyph = IndicatorPresentation.GlyphOf(type);

        if (source is null || reading is null)
        {
            return new IndicatorCardViewModel(
                id, glyph, name, null, StatusPalette.Name(null), "—", string.Empty,
                "Registra un check-up para ver el estado de este indicador.", false);
        }

        return new IndicatorCardViewModel(
            id,
            glyph,
            name,
            reading.Evaluation.Status,
            StatusPalette.Name(reading.Evaluation.Status),
            IndicatorPresentation.FormatValue(reading.Input),
            string.Create(CultureInfo.CurrentCulture, $"Última medición: {source.RecordedAt.ToLocalTime():g}"),
            IndicatorPresentation.GuidanceOf(reading.Evaluation),
            true);
    }
}
