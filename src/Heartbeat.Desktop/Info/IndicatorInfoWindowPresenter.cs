using System.Windows;

using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Info;

/// <summary>Muestra el panel de información en una ventana no modal, única y reutilizada.</summary>
public sealed class IndicatorInfoWindowPresenter(IServiceProvider services) : IIndicatorInfoPresenter
{
    private IndicatorInfoWindow? _window;

    public void Show(string? indicatorId = null)
    {
        if (_window is null)
        {
            IndicatorInfoWindow window = new(services.GetRequiredService<IndicatorInfoViewModel>())
            {
                Owner = Application.Current?.MainWindow,
            };
            window.Closed += (_, _) => _window = null;
            _window = window;
        }

        _window.ViewModel.Select(indicatorId);

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Show();
        _window.Activate();
    }
}
