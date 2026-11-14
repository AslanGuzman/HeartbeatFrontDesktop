using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Navigation;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Pantalla inicial. Por ahora es un marcador de posición: el login real, online y offline (#27),
/// sustituirá <see cref="ContinueCommand"/> por la autenticación.
/// </summary>
public sealed partial class LoginViewModel(INavigationService navigation) : PageViewModel
{
    public override string Title => "Iniciar sesión";

    public override string? Description => "El acceso con usuario y contraseña, también sin conexión, llega con el ticket #27.";

    public override bool ShowsNavigation => false;

    [RelayCommand]
    private void Continue() => navigation.NavigateTo<DashboardViewModel>();
}
