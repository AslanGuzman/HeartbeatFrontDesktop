using Heartbeat.Desktop.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Navigation;

/// <summary>
/// Crea cada página dentro de su propio <see cref="IServiceScope"/>. Al salir de la página se libera el scope,
/// lo que elimina el ViewModel (si es <see cref="IDisposable"/>) y evita que el contenedor raíz retenga
/// instancias transitorias durante toda la vida de la aplicación.
/// </summary>
public sealed class NavigationService(IServiceProvider services) : INavigationService
{
    private IServiceScope? _currentScope;

    public PageViewModel? CurrentPage { get; private set; }

    public event EventHandler? CurrentPageChanged;

    public PageViewModel NavigateTo(Type pageType, Action<PageViewModel>? initialize = null)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        if (!typeof(PageViewModel).IsAssignableFrom(pageType) || pageType.IsAbstract)
        {
            throw new ArgumentException($"{pageType} no es un {nameof(PageViewModel)} concreto.", nameof(pageType));
        }

        if (CurrentPage is not null && initialize is null && CurrentPage.GetType() == pageType)
        {
            return CurrentPage;
        }

        IServiceScope scope = services.CreateScope();
        PageViewModel page;
        try
        {
            page = (PageViewModel)scope.ServiceProvider.GetRequiredService(pageType);
            initialize?.Invoke(page);
        }
        catch
        {
            // Si la página no se puede crear, la actual sigue intacta.
            scope.Dispose();
            throw;
        }

        IServiceScope? previousScope = _currentScope;
        _currentScope = scope;
        CurrentPage = page;

        CurrentPageChanged?.Invoke(this, EventArgs.Empty);

        previousScope?.Dispose();
        return page;
    }
}
