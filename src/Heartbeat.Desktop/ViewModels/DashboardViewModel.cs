using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Info;

namespace Heartbeat.Desktop.ViewModels;

public sealed partial class DashboardViewModel(IIndicatorInfoProvider info, IIndicatorInfoPresenter infoPresenter) : PageViewModel
{
    public override string Title => "Dashboard";

    public override string? Description => "El estado actual de los 6 indicadores llega con el ticket #30.";

    /// <summary>Indicadores con ficha informativa (por qué importa, cómo medir, riesgos y fuentes).</summary>
    public IReadOnlyList<IndicatorInfo> Indicators => info.Indicators;

    /// <summary>Abre el panel de información; con un id lo deja directamente en ese indicador.</summary>
    [RelayCommand]
    private void ShowInfo(string? indicatorId) => infoPresenter.Show(indicatorId);
}
