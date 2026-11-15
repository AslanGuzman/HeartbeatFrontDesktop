using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Navigation;

/// <summary>
/// Navegación "ViewModel primero": se pide un <see cref="PageViewModel"/> y la vista se resuelve
/// con los <c>DataTemplate</c> de <c>Views/PageTemplates.xaml</c>.
/// </summary>
public interface INavigationService
{
    /// <summary>Página que se está mostrando; <c>null</c> hasta la primera navegación.</summary>
    PageViewModel? CurrentPage { get; }

    /// <summary>Se produce después de que <see cref="CurrentPage"/> cambia.</summary>
    event EventHandler? CurrentPageChanged;

    /// <summary>
    /// Navega a una página nueva del tipo indicado. <paramref name="initialize"/> se ejecuta antes de mostrarla
    /// y sirve para pasarle parámetros (por ejemplo, el check-up recién guardado a la pantalla de resultado).
    /// Si ya se está mostrando una página de ese tipo y no hay <paramref name="initialize"/>, se conserva
    /// la actual para no perder lo que el usuario estaba escribiendo.
    /// </summary>
    PageViewModel NavigateTo(Type pageType, Action<PageViewModel>? initialize = null);

    /// <summary>
    /// Vuelve a crear la página actual con los mismos parámetros (por ejemplo, para que recalcule sus textos al
    /// cambiar el idioma). La implementación por defecto no hace nada.
    /// </summary>
    void Reload()
    {
    }
}

public static class NavigationServiceExtensions
{
    public static TViewModel NavigateTo<TViewModel>(this INavigationService navigation, Action<TViewModel>? initialize = null)
        where TViewModel : PageViewModel
    {
        ArgumentNullException.ThrowIfNull(navigation);

        Action<PageViewModel>? wrapped = initialize is null ? null : page => initialize((TViewModel)page);
        return (TViewModel)navigation.NavigateTo(typeof(TViewModel), wrapped);
    }
}
