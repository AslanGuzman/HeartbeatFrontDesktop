namespace Heartbeat.Desktop.Info;

/// <summary>
/// Abre el panel de información. Es el punto de entrada común para el dashboard y para el formulario de check-up
/// (#30): se muestra en una ventana aparte para que el astronauta pueda leerlo sin salir del formulario ni perder
/// lo que ya ha escrito.
/// </summary>
public interface IIndicatorInfoPresenter
{
    /// <summary>Muestra el panel. Si ya está abierto lo trae al frente; con <paramref name="indicatorId"/> lo deja en ese indicador.</summary>
    void Show(string? indicatorId = null);
}
