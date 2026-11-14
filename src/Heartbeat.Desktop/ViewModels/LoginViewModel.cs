using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Pantalla inicial. Valida con <see cref="IAuthenticator"/>: hoy es el acceso de demostración; el login real,
/// online y offline (#27), solo tiene que sustituir esa implementación.
/// </summary>
public sealed partial class LoginViewModel : PageViewModel
{
    private readonly INavigationService _navigation;
    private readonly IAuthenticator _authenticator;
    private readonly ISessionContext _session;

    public LoginViewModel(INavigationService navigation, IAuthenticator authenticator, ISessionContext session)
    {
        _navigation = navigation;
        _authenticator = authenticator;
        _session = session;
        Username = string.Empty;
    }

    public override string Title => "Iniciar sesión";

    public override string? Description => "Entra a tu perfil de astronauta. También funciona sin enlace con la Tierra.";

    public override bool ShowsNavigation => false;

    public string DemoHint => $"Usuarios de demostración: {string.Join(", ", DemoAuthenticator.Accounts.Select(a => a.UserName))} · contraseña: {DemoAuthenticator.DemoPassword}";

    [ObservableProperty]
    public partial string Username { get; set; }

    /// <summary>Mensaje de error del último intento; <c>null</c> si no hay.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <param name="password">La contraseña llega desde el PasswordBox, que no admite enlace de datos.</param>
    [RelayCommand]
    private void SignIn(string? password)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(password))
        {
            Error = "Escribe tu usuario y tu contraseña.";
            return;
        }

        if (!_authenticator.TryAuthenticate(Username, password, out SessionUser? user))
        {
            Error = "Usuario o contraseña incorrectos.";
            return;
        }

        Error = null;
        _session.SignIn(user);
        _navigation.NavigateTo<DashboardViewModel>();
    }
}
