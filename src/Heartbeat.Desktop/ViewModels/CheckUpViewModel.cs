using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Una de las cinco opciones del estado de ánimo (1 muy mal – 5 muy bien).</summary>
public sealed partial class MoodOptionViewModel(int value, string label) : ObservableObject
{
    public int Value { get; } = value;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Check-up de rutina: entre 1 y 6 indicadores en una sola sesión con un único instante (MR-13), validados con
/// <see cref="IHealthEvaluator.Validate"/>, evaluados al instante en el equipo (MR-03, MR-04, MR-10) y guardados en la
/// base local como pendientes de sincronizar. Si algo es crítico se muestra el aviso de escalamiento (MR-06).
/// </summary>
public sealed partial class CheckUpViewModel : PageViewModel
{
    public const double SleepStep = 0.5;
    public const double SleepMaximum = 24;

    private readonly IHealthRecordStore _store;
    private readonly ISessionContext _session;
    private readonly IHealthEvaluator _evaluator;
    private readonly IIndicatorInfoProvider _info;
    private readonly IIndicatorInfoPresenter _infoPresenter;
    private readonly INavigationService _navigation;
    private bool _syncingSleep;

    public CheckUpViewModel(
        IHealthRecordStore store,
        ISessionContext session,
        IHealthEvaluator evaluator,
        IIndicatorInfoProvider info,
        IIndicatorInfoPresenter infoPresenter,
        INavigationService navigation,
        BodyMeasurementsViewModel body)
    {
        _navigation = navigation;
        Body = body;
        _store = store;
        _session = session;
        _evaluator = evaluator;
        _info = info;
        _infoPresenter = infoPresenter;

        MoodOptions =
        [
            new(1, "Muy mal"),
            new(2, "Mal"),
            new(3, "Normal"),
            new(4, "Bien"),
            new(5, "Muy bien"),
        ];

        Systolic = string.Empty;
        Diastolic = string.Empty;
        HeartRate = string.Empty;
        BloodSugar = string.Empty;
        Temperature = string.Empty;
        SleepHours = string.Empty;
        Note = string.Empty;
    }

    public override string Title => "Check-up";

    /// <summary>Panel de medidas corporales que acompaña al formulario.</summary>
    public BodyMeasurementsViewModel Body { get; }

    /// <summary>Máximo de caracteres de la nota (contrato: 500).</summary>
    public int NoteMaxLength => LocalHealthRecordStore.MaxNoteLength;

    // Cada campo puede dejarse vacío: ese indicador se omite del check-up.
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

    /// <summary>Horas de sueño escritas con el teclado; se mantiene sincronizado con <see cref="SleepSlider"/>.</summary>
    [ObservableProperty]
    public partial string SleepHours { get; set; }

    /// <summary>Horas de sueño con el deslizador (paso de 0,5 h).</summary>
    [ObservableProperty]
    public partial double SleepSlider { get; set; }

    /// <summary>Estado de ánimo de 1 (muy mal) a 5 (muy bien); <c>null</c> si se omite.</summary>
    [ObservableProperty]
    public partial int? Mood { get; private set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    public IReadOnlyList<MoodOptionViewModel> MoodOptions { get; }

    /// <summary>Error de validación del último intento; <c>null</c> si no hay.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Elige un estado de ánimo; elegir el que ya está elegido lo quita (se omite).</summary>
    [RelayCommand]
    private void SelectMood(int? value)
    {
        Mood = Mood == value ? null : value;
        foreach (MoodOptionViewModel option in MoodOptions)
        {
            option.IsSelected = option.Value == Mood;
        }
    }

    /// <summary>Abre el panel de información en el indicador del campo (accesible desde el formulario).</summary>
    [RelayCommand]
    private void ShowInfo(string? indicatorId) => _infoPresenter.Show(indicatorId);

    [RelayCommand]
    private void Save()
    {
        Error = null;

        if (_session.CurrentUser is not { } user)
        {
            Error = "Inicia sesión para guardar un check-up.";
            return;
        }

        List<string> problems = [];
        List<ReadingInput> inputs = BuildInputs(problems);

        if (problems.Count == 0 && inputs.Count == 0)
        {
            Error = "Registra al menos un indicador. Puedes omitir los que no midas hoy.";
            return;
        }

        foreach (ReadingInput input in inputs)
        {
            if (_evaluator.Validate(input).Count > 0)
            {
                problems.Add($"{NameOf(input.Indicator)} (fuera de lo que se puede medir)");
            }
        }

        if (problems.Count > 0)
        {
            Error = "Revisa: " + string.Join("; ", problems) + ".";
            return;
        }

        CheckUpEvaluation evaluation = _evaluator.Evaluate(inputs);
        CheckUpRecord saved = _store.AddCheckUp(user.Id, inputs, evaluation, Note);

        // El resultado (estado, guía, escalamiento y reporte) se muestra en su propia pantalla (#29).
        _navigation.NavigateTo<CheckUpResultViewModel>(result => result.Load(saved));
    }

    partial void OnSleepHoursChanged(string value)
    {
        if (_syncingSleep)
        {
            return;
        }

        // Si lo escrito es un número válido, el deslizador lo sigue (ajustado al paso de 0,5 h).
        _syncingSleep = true;
        SleepSlider = TryParse(value, out decimal hours) && hours is >= 0 and <= (decimal)SleepMaximum
            ? Math.Round((double)hours / SleepStep) * SleepStep
            : 0;
        _syncingSleep = false;
    }

    partial void OnSleepSliderChanged(double value)
    {
        if (_syncingSleep)
        {
            return;
        }

        // Mover el deslizador escribe el valor en el campo de texto.
        _syncingSleep = true;
        SleepHours = value.ToString("0.#", CultureInfo.CurrentCulture);
        _syncingSleep = false;
    }

    private List<ReadingInput> BuildInputs(List<string> problems)
    {
        List<ReadingInput> inputs = [];

        // Presión: sistólica y diastólica van juntas.
        bool hasSys = !string.IsNullOrWhiteSpace(Systolic);
        bool hasDia = !string.IsNullOrWhiteSpace(Diastolic);
        if (hasSys != hasDia)
        {
            problems.Add($"{NameOf(IndicatorType.BloodPressure)} (escribe la sistólica y la diastólica)");
        }
        else if (hasSys)
        {
            if (TryParse(Systolic, out decimal sys) && TryParse(Diastolic, out decimal dia))
            {
                inputs.Add(new ReadingInput(IndicatorType.BloodPressure, sys, dia));
            }
            else
            {
                problems.Add($"{NameOf(IndicatorType.BloodPressure)} (escribe números)");
            }
        }

        AddSingle(inputs, problems, IndicatorType.HeartRate, HeartRate);
        AddSingle(inputs, problems, IndicatorType.BloodSugar, BloodSugar);
        AddSingle(inputs, problems, IndicatorType.BodyTemperature, Temperature);
        AddSingle(inputs, problems, IndicatorType.Sleep, SleepHours);

        if (Mood is { } mood)
        {
            inputs.Add(new ReadingInput(IndicatorType.Mood, mood));
        }

        return inputs;
    }

    private void AddSingle(List<ReadingInput> inputs, List<string> problems, IndicatorType type, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (TryParse(text, out decimal value))
        {
            inputs.Add(new ReadingInput(type, value));
        }
        else
        {
            problems.Add($"{NameOf(type)} (escribe un número)");
        }
    }

    private string NameOf(IndicatorType type)
    {
        string id = IndicatorPresentation.IdOf(type);
        return _info.Find(id)?.Name ?? id;
    }

    // Se acepta la coma decimal ("36,6") además del punto, sea cual sea la cultura del equipo.
    private static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}
