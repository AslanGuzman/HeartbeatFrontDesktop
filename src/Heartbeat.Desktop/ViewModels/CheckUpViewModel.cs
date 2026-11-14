using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Check-up de rutina: las seis lecturas entran juntas como una sesión (MR-13), se evalúan en local con las reglas
/// compartidas (MR-03, MR-04, MR-10) y, si algo es crítico, se muestra el aviso de escalamiento (MR-06).
/// </summary>
public sealed partial class CheckUpViewModel : PageViewModel
{
    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IHealthEvaluator _evaluator;
    private readonly IIndicatorInfoProvider _info;
    private readonly IIndicatorInfoPresenter _infoPresenter;
    private readonly IConnectivityService _connectivity;

    public CheckUpViewModel(
        IHealthRecordStore store,
        ISessionContext session,
        IHealthEvaluator evaluator,
        IIndicatorInfoProvider info,
        IIndicatorInfoPresenter infoPresenter,
        IConnectivityService connectivity,
        BodyMeasurementsViewModel body)
    {
        Body = body;
        _store = store;
        _session = session;
        _evaluator = evaluator;
        _info = info;
        _infoPresenter = infoPresenter;
        _connectivity = connectivity;

        Systolic = string.Empty;
        Diastolic = string.Empty;
        HeartRate = string.Empty;
        BloodSugar = string.Empty;
        Temperature = string.Empty;
        SleepHours = string.Empty;
        Mood = 3;
    }

    public override string Title => "Check-up";

    /// <summary>Panel de medidas corporales que acompaña al formulario.</summary>
    public BodyMeasurementsViewModel Body { get; }

    [ObservableProperty]
    public partial string Systolic { get; set; }

    [ObservableProperty]
    public partial string Diastolic { get; set; }

    [ObservableProperty]
    public partial string HeartRate { get; set; }

    [ObservableProperty]
    public partial string BloodSugar { get; set; }

    [ObservableProperty]
    public partial string Temperature { get; set; }

    [ObservableProperty]
    public partial string SleepHours { get; set; }

    /// <summary>Estado de ánimo de 1 (muy mal) a 5 (muy bien).</summary>
    [ObservableProperty]
    public partial double Mood { get; set; }

    /// <summary>Error de validación del último intento; <c>null</c> si no hay.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Aviso de escalamiento (MR-06); <c>null</c> si ninguna lectura requiere avisar.</summary>
    [ObservableProperty]
    public partial string? Escalation { get; private set; }

    /// <summary>Confirmación tras guardar; <c>null</c> mientras no se haya guardado.</summary>
    [ObservableProperty]
    public partial string? SavedText { get; private set; }

    public ObservableCollection<ReadingRowViewModel> Results { get; } = [];

    /// <summary>Abre el panel de información en el indicador del campo (accesible desde el formulario).</summary>
    [RelayCommand]
    private void ShowInfo(string? indicatorId) => _infoPresenter.Show(indicatorId);

    [RelayCommand]
    private void Save()
    {
        Error = null;
        Escalation = null;
        SavedText = null;
        Results.Clear();

        if (_session.CurrentUser is not { } user)
        {
            Error = "Inicia sesión para guardar un check-up.";
            return;
        }

        if (!TryBuildInputs(out List<ReadingInput> inputs))
        {
            Error = "Revisa los valores: todos los campos son numéricos y el check-up se guarda completo.";
            return;
        }

        List<string> invalid = inputs.Where(i => _evaluator.Validate(i).Count > 0).Select(NameOf).ToList();
        if (invalid.Count > 0)
        {
            Error = $"Revisa {string.Join(", ", invalid)}: el valor está fuera de lo que se puede medir.";
            return;
        }

        CheckUpEvaluation evaluation = _evaluator.Evaluate(inputs);
        _store.AddCheckUp(user.UserName, inputs, evaluation);

        for (int i = 0; i < inputs.Count; i++)
        {
            ReadingEvaluation reading = evaluation.Readings[i];
            Results.Add(new ReadingRowViewModel(
                NameOf(inputs[i]),
                IndicatorPresentation.FormatValue(inputs[i]),
                reading.Status,
                StatusPalette.Name(reading.Status),
                IndicatorPresentation.GuidanceOf(reading)));
        }

        if (evaluation.Readings.Any(r => r.Escalation?.Contact == EscalationContact.CrewMedicalOfficer))
        {
            Escalation = "Resultado crítico: avisa ahora a tu oficial médico de la tripulación. "
                       + "Queda marcado para los cirujanos de vuelo y se enviará en la próxima sincronización.";
        }

        SavedText = _connectivity.IsOnline
            ? "Check-up guardado y enviado a control de misión."
            : "Check-up guardado. Se sincronizará cuando haya enlace.";
    }

    private string NameOf(ReadingInput input)
    {
        string id = IndicatorPresentation.IdOf(input.Indicator);
        return _info.Find(id)?.Name ?? id;
    }

    private bool TryBuildInputs(out List<ReadingInput> inputs)
    {
        inputs = [];
        if (!TryParse(Systolic, out decimal systolic) ||
            !TryParse(Diastolic, out decimal diastolic) ||
            !TryParse(HeartRate, out decimal heartRate) ||
            !TryParse(BloodSugar, out decimal bloodSugar) ||
            !TryParse(Temperature, out decimal temperature) ||
            !TryParse(SleepHours, out decimal sleepHours))
        {
            return false;
        }

        inputs =
        [
            new(IndicatorType.BloodPressure, systolic, diastolic),
            new(IndicatorType.HeartRate, heartRate),
            new(IndicatorType.BloodSugar, bloodSugar),
            new(IndicatorType.BodyTemperature, temperature),
            new(IndicatorType.Sleep, sleepHours),
            new(IndicatorType.Mood, Math.Round((decimal)Mood)),
        ];
        return true;
    }

    // Se acepta la coma decimal ("36,6") además del punto, sea cual sea la cultura del equipo.
    private static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}
