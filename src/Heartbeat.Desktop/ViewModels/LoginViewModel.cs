using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Auth;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>
/// Pantalla de acceso (MR-12). Con conexión valida contra la Crew API; sin conexión, contra la cuenta guardada en
/// este equipo. El primer acceso de cada astronauta debe ser en línea.
/// </summary>
public sealed partial class LoginViewModel : PageViewModel, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly IAuthService _auth;
    private readonly IConnectivityService _connectivity;
    private readonly CrewApiOptions _options;
    private readonly SynchronizationContext? _uiContext;

    /// <param name="uiContext">Contexto al que se reenvían los cambios de conexión (por defecto, el del hilo que crea la pantalla).</param>
    public LoginViewModel(
        INavigationService navigation,
        IAuthService auth,
        IConnectivityService connectivity,
        CrewApiOptions options,
        SynchronizationContext? uiContext = null)
    {
        _navigation = navigation;
        _auth = auth;
        _connectivity = connectivity;
        _options = options;
        _uiContext = uiContext ?? SynchronizationContext.Current;

        Username = string.Empty;
        _connectivity.IsOnlineChanged += OnConnectivityChanged;
    }

    public override string Title => Loc.T("login.title");

    public override string? Description => Loc.T("login.desc");

    public override bool ShowsNavigation => false;

    /// <summary>El formulario conserva lo escrito al cambiar el idioma: sus textos son propiedades calculadas.</summary>
    public override bool RebuildsOnLanguageChange => false;

    /// <summary>Texto de ayuda en modo demostración; <c>null</c> con el servidor real.</summary>
    public string? DemoHint => _options.IsDemo
        ? Loc.T("login.demoHint", string.Join(", ", DemoAccounts.All.Select(a => a.UserName)), DemoAccounts.Password)
        : null;

    public bool IsOnline => _connectivity.IsOnline;

    /// <summary>Aviso cuando no hay enlace: qué se puede hacer y qué no.</summary>
    public string? OfflineNotice => IsOnline
        ? null
        : Loc.T("login.offlineNotice");

    [ObservableProperty]
    public partial string Username { get; set; }

    /// <summary>Mensaje de error del último intento; <c>null</c> si no hay.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignInText))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; private set; }

    public bool IsNotBusy => !IsBusy;

    public string SignInText => IsBusy ? Loc.T("login.signingIn") : Loc.T("login.signIn");

    public void Dispose() => _connectivity.IsOnlineChanged -= OnConnectivityChanged;

    /// <param name="password">La contraseña llega desde el PasswordBox, que no admite enlace de datos.</param>
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SignInAsync(string? password)
    {
        IsBusy = true;
        Error = null;
        try
        {
            SignInResult result = await _auth.SignInAsync(Username, password ?? string.Empty);
            if (result.Succeeded)
            {
                _navigation.NavigateTo<DashboardViewModel>();
            }
            else
            {
                Error = result.Message;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = Loc.T("login.err.unexpected");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => SignInCommand.NotifyCanExecuteChanged();

    private void OnConnectivityChanged(object? sender, EventArgs e)
    {
        // El evento llega en un hilo del pool: las propiedades se notifican en el hilo de la interfaz.
        if (_uiContext is null)
        {
            Notify();
        }
        else
        {
            _uiContext.Post(static state => ((LoginViewModel)state!).Notify(), this);
        }
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(OfflineNotice));
    }
}
