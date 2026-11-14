using System.Globalization;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Una lectura del resultado: estado y etiqueta con color, y la guía si no está en rango (MR-03, MR-04).</summary>
public sealed record ResultReadingViewModel(
    string Id,
    string Name,
    string ValueText,
    HealthStatus Status,
    string StatusText,
    string Label,
    IReadOnlyList<string> GuidanceItems)
{
    public bool HasGuidance => GuidanceItems.Count > 0;

    /// <summary>Texto del chip: el estado y, si aporta algo distinto, la etiqueta de la banda («Vigilancia · Fast»).</summary>
    public string ChipText =>
        string.IsNullOrWhiteSpace(Label) || string.Equals(Label, StatusText, StringComparison.OrdinalIgnoreCase)
            ? StatusText
            : $"{StatusText} · {Label}";

    /// <summary>Las lecturas en rango no llevan guía: se muestra una frase corta.</summary>
    public bool IsNormal => Status == HealthStatus.Normal;
}

/// <summary>Aviso destacado cuando hay lecturas críticas (MR-06): a quién contactar y por qué.</summary>
public sealed record EscalationViewModel(string Title, string Contact, string Message, string Indicators);

/// <summary>
/// Pantalla que se abre al guardar un check-up: dice qué hacer a continuación. Muestra el estado de cada lectura,
/// la guía de las que no están en rango, el aviso de escalamiento si hay críticos y permite enviar un reporte a
/// control de misión.
/// </summary>
public sealed partial class CheckUpResultViewModel : PageViewModel
{
    private readonly INavigationService _navigation;
    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IIndicatorInfoProvider _info;
    private readonly IIndicatorInfoPresenter _infoPresenter;
    private readonly IConnectivityService _connectivity;
    private CheckUpRecord? _checkUp;

    public CheckUpResultViewModel(
        INavigationService navigation,
        IHealthRecordStore store,
        ISessionContext session,
        IIndicatorInfoProvider info,
        IIndicatorInfoPresenter infoPresenter,
        IConnectivityService connectivity)
    {
        _navigation = navigation;
        _store = store;
        _session = session;
        _info = info;
        _infoPresenter = infoPresenter;
        _connectivity = connectivity;

        Readings = [];
        OverallText = string.Empty;
        SummaryText = string.Empty;
    }

    public override string Title => "Resultado del check-up";

    // El resultado cuelga de Check-up en el menú.
    public override Type? NavigationSection => typeof(CheckUpViewModel);

    public IReadOnlyList<ResultReadingViewModel> Readings { get; private set; }

    [ObservableProperty]
    public partial HealthStatus OverallStatus { get; private set; }

    [ObservableProperty]
    public partial string OverallText { get; private set; }

    /// <summary>Por ejemplo: «Guardado a las 12:00 · 3 indicadores · Se sincronizará cuando haya enlace».</summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; }

    /// <summary>Aviso destacado de escalamiento; <c>null</c> si no hay lecturas críticas.</summary>
    [ObservableProperty]
    public partial EscalationViewModel? Escalation { get; private set; }

    /// <summary>Recordatorio cuando hay lecturas en vigilancia; <c>null</c> si no las hay.</summary>
    [ObservableProperty]
    public partial string? WatchNotice { get; private set; }

    [ObservableProperty]
    public partial string? Note { get; private set; }

    /// <summary>Estado del reporte a control de misión; <c>null</c> mientras no se haya enviado.</summary>
    [ObservableProperty]
    public partial string? ReportText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendReportCommand))]
    public partial bool ReportSent { get; private set; }

    public bool HasEscalation => Escalation is not null;

    /// <summary>Carga el check-up recién guardado (se llama desde la navegación, antes de mostrar la pantalla).</summary>
    public void Load(CheckUpRecord checkUp)
    {
        ArgumentNullException.ThrowIfNull(checkUp);
        _checkUp = checkUp;

        Readings = checkUp.Readings.Select(ToRow).ToList();
        OnPropertyChanged(nameof(Readings));

        OverallStatus = checkUp.OverallStatus;
        OverallText = $"Estado general: {StatusPalette.Name(checkUp.OverallStatus)}";
        Note = checkUp.Note;

        string count = Readings.Count == 1 ? "1 indicador" : $"{Readings.Count} indicadores";
        string when = checkUp.RecordedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        SummaryText = _connectivity.IsOnline
            ? $"Guardado a las {when} · {count} · Enviado a control de misión"
            : $"Guardado a las {when} · {count} · Se sincronizará cuando haya enlace";

        List<ResultReadingViewModel> critical = Readings.Where(r => r.Status == HealthStatus.Critical).ToList();
        bool contactOfficer = checkUp.Readings.Any(r => r.Evaluation.Escalation?.Contact == EscalationContact.CrewMedicalOfficer);
        Escalation = critical.Count > 0 && contactOfficer
            ? new EscalationViewModel(
                "Resultado crítico",
                "Tu oficial médico de la tripulación",
                "Avísale ahora. Este resultado queda marcado para los cirujanos de vuelo en tierra y les llegará en la próxima sincronización.",
                string.Join(", ", critical.Select(r => r.Name)))
            : null;
        OnPropertyChanged(nameof(HasEscalation));

        WatchNotice = Readings.Any(r => r.Status == HealthStatus.Watch)
            ? "Hay lecturas en vigilancia: vuelve a medirlas en tu próximo check-up. Control de misión verá el resultado tras la próxima sincronización."
            : null;

        ReportSent = _store.HasReport(checkUp.Id);
        ReportText = ReportSent ? "Ya enviaste un reporte de este check-up a control de misión." : null;
    }

    /// <summary>Envía a control de misión un reporte que adjunta este check-up (MR-11). Sin enlace queda en cola.</summary>
    [RelayCommand(CanExecute = nameof(CanSendReport))]
    private void SendReport()
    {
        // Execute no consulta CanExecute: se protege aquí que un check-up tenga un solo reporte.
        if (_checkUp is null || ReportSent || _session.CurrentUser is not { } user)
        {
            return;
        }

        string subject = $"Check-up {_checkUp.RecordedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · {StatusPalette.Name(_checkUp.OverallStatus)}";
        _store.QueueReport(user.Id, _checkUp.Id, subject, BuildReportBody(_checkUp));

        ReportSent = true;
        ReportText = _connectivity.IsOnline
            ? "Reporte enviado a control de misión."
            : "Reporte en cola: saldrá hacia control de misión cuando haya enlace.";
    }

    private bool CanSendReport() => _checkUp is not null && !ReportSent;

    [RelayCommand]
    private void NewCheckUp() => _navigation.NavigateTo<CheckUpViewModel>();

    [RelayCommand]
    private void ShowHistory() => _navigation.NavigateTo<HistoryViewModel>();

    [RelayCommand]
    private void ShowInfo(string? indicatorId) => _infoPresenter.Show(indicatorId);

    private ResultReadingViewModel ToRow(ReadingRecord reading)
    {
        string id = IndicatorPresentation.IdOf(reading.Input.Indicator);
        ReadingEvaluation evaluation = reading.Evaluation;
        return new ResultReadingViewModel(
            id,
            _info.Find(id)?.Name ?? id,
            IndicatorPresentation.FormatValue(reading.Input),
            evaluation.Status,
            StatusPalette.Name(evaluation.Status),
            evaluation.Label,
            evaluation.Status == HealthStatus.Normal ? [] : evaluation.Guidance);
    }

    private string BuildReportBody(CheckUpRecord checkUp)
    {
        StringBuilder text = new();
        text.Append(CultureInfo.CurrentCulture, $"Estado general: {StatusPalette.Name(checkUp.OverallStatus)}.");
        foreach (ResultReadingViewModel reading in Readings)
        {
            text.Append(CultureInfo.CurrentCulture, $"\n- {reading.Name}: {reading.ValueText} — {reading.StatusText} ({reading.Label})");
        }

        if (!string.IsNullOrWhiteSpace(checkUp.Note))
        {
            text.Append("\nNota: ").Append(checkUp.Note);
        }

        return text.ToString();
    }
}
