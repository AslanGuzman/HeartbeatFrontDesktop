using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Una palabra técnica con su significado en lenguaje simple.</summary>
public sealed record GlossaryTermViewModel(string Term, string Meaning);

/// <summary>
/// Glosario corto de los términos que aparecen en el panel (mSv, CO₂, SANS, PVT…). Los textos están en la tabla de
/// idiomas; no define umbrales ni fórmulas (esos viven en el catálogo compartido).
/// </summary>
public static class Glossary
{
    private static readonly string[] Keys = ["msv", "co2", "sans", "pvt", "iop", "logmar", "gcr", "spe", "mmhg"];

    public static IReadOnlyList<GlossaryTermViewModel> Terms() =>
        Keys.Select(k => new GlossaryTermViewModel(Loc.T($"gloss.{k}.term"), Loc.T($"gloss.{k}.meaning"))).ToList();
}

/// <summary>
/// Introducción de 3 pasos que se muestra la primera vez (HB-FE-01): qué son los cinco semáforos, qué significan los
/// colores y que los datos son simulados y no un diagnóstico. «Saltar» y «Listo» la marcan como vista para ese
/// astronauta; «Ver introducción» la vuelve a abrir sin cambiar nada guardado.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    public const int StepCount = 3;

    private readonly IOnboardingStore? _store;
    private readonly Guid? _astronautId;

    public OnboardingViewModel(IOnboardingStore? store, Guid? astronautId)
    {
        _store = store;
        _astronautId = astronautId;
        IsOpen = store is not null && astronautId is { } id && !store.IsCompleted(id);
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial int StepIndex { get; set; }

    [ObservableProperty]
    public partial string StepTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StepBody { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NextText { get; set; } = string.Empty;

    public bool CanGoBack => StepIndex > 0;

    public bool IsLastStep => StepIndex == StepCount - 1;

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep)
        {
            Finish();
            return;
        }

        StepIndex++;
        Refresh();
    }

    [RelayCommand]
    private void Back()
    {
        if (StepIndex > 0)
        {
            StepIndex--;
            Refresh();
        }
    }

    /// <summary>Cierra la introducción y no la vuelve a mostrar a este astronauta.</summary>
    [RelayCommand]
    private void Finish()
    {
        if (_astronautId is { } id)
        {
            _store?.MarkCompleted(id);
        }

        IsOpen = false;
    }

    /// <summary>La abre otra vez desde el primer paso.</summary>
    [RelayCommand]
    private void Show()
    {
        StepIndex = 0;
        Refresh();
        IsOpen = true;
    }

    private void Refresh()
    {
        int step = StepIndex + 1;
        StepTitle = Loc.T($"onb.step{step}.title");
        StepBody = Loc.T($"onb.step{step}.body");
        ProgressText = Loc.T("onb.progress", step, StepCount);
        NextText = Loc.T(step == StepCount ? "onb.done" : "onb.next");
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
    }
}
