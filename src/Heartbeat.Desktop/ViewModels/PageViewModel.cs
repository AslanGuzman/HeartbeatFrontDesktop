using CommunityToolkit.Mvvm.ComponentModel;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Base de todas las pantallas que se muestran en la zona de contenido de la ventana principal.</summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>Título de la pantalla; también es la etiqueta de su botón en el menú lateral.</summary>
    public abstract string Title { get; }

    /// <summary>Texto secundario opcional. Las pantallas aún no implementadas lo usan para explicar qué traerán.</summary>
    public virtual string? Description => null;

    /// <summary>
    /// Pantalla del menú a la que pertenece esta página (por ejemplo, el resultado de un check-up pertenece a Check-up);
    /// <c>null</c> si la página es su propia entrada del menú.
    /// </summary>
    public virtual Type? NavigationSection => null;

    /// <summary>Indica si el menú lateral se muestra con esta pantalla (el login lo oculta).</summary>
    public virtual bool ShowsNavigation => true;
}
