using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Panel de información: lista de indicadores, ficha del seleccionado y enlaces a sus fuentes.</summary>
public sealed partial class IndicatorInfoViewModel : ObservableObject
{
    private readonly IIndicatorInfoProvider _provider;
    private readonly IUrlLauncher _launcher;

    public IndicatorInfoViewModel(IIndicatorInfoProvider provider, IUrlLauncher launcher)
    {
        _provider = provider;
        _launcher = launcher;
        SelectedIndicator = provider.Indicators[0];
    }

    public IReadOnlyList<IndicatorInfo> Indicators => _provider.Indicators;

    public string RangesVersionText => Loc.T("info.rangesVersion", _provider.RangesVersion);

    public string DisclaimerText => AppText.MedicalDisclaimer;

    [ObservableProperty]
    public partial IndicatorInfo SelectedIndicator { get; set; }

    /// <summary>Se rellena cuando un enlace no se pudo abrir; se limpia al cambiar de indicador o al reintentar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkError))]
    public partial string? LinkErrorText { get; private set; }

    public bool HasLinkError => LinkErrorText is not null;

    /// <summary>Deja el panel en el indicador dado. Un id desconocido se ignora y se conserva la selección actual.</summary>
    public void Select(string? indicatorId)
    {
        if (indicatorId is not null && _provider.Find(indicatorId) is { } indicator)
        {
            SelectedIndicator = indicator;
        }
    }

    [RelayCommand]
    private void OpenSource(InfoSource? source)
    {
        if (source is null)
        {
            return;
        }

        LinkErrorText = _launcher.TryOpen(source.Url)
            ? null
            : Loc.T("info.linkFailed", source.Url.AbsoluteUri);
    }

    partial void OnSelectedIndicatorChanged(IndicatorInfo value) => LinkErrorText = null;
}
