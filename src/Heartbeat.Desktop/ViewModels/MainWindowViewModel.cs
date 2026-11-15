using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Info;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services;
using Heartbeat.Desktop.Services.Auth;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Estructura de la ventana: barra superior, rail de navegación, página actual y barra de estado.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly ISessionContext _session;
    private readonly IAuthService _auth;
    private readonly ISimulatedLink _link;
    private readonly IIndicatorInfoPresenter _infoPresenter;

    public MainWindowViewModel(
        INavigationService navigation,
        StatusBarViewModel statusBar,
        ISessionContext session,
        IAuthService auth,
        ISimulatedLink link,
        IIndicatorInfoPresenter infoPresenter)
    {
        _navigation = navigation;
        _session = session;
        _auth = auth;
        _link = link;
        _infoPresenter = infoPresenter;
        StatusBar = statusBar;

        NavigationItems =
        [
            new NavigationItemViewModel("nav.dashboard", "", typeof(DashboardViewModel)),
            new NavigationItemViewModel("nav.checkUp", "", typeof(CheckUpViewModel)),
            new NavigationItemViewModel("nav.history", "", typeof(HistoryViewModel)),
            new NavigationItemViewModel("nav.messages", "", typeof(MessagesViewModel)),
            new NavigationItemViewModel("nav.radiation", "\uE7BA", typeof(RadiationViewModel)),
            new NavigationItemViewModel("nav.vision", "", typeof(VisionViewModel)),
            new NavigationItemViewModel("nav.watch", "\uE95E", typeof(WatchViewModel)),
            new NavigationItemViewModel("nav.profile", "", typeof(ProfileViewModel)),
        ];

        _navigation.CurrentPageChanged += OnCurrentPageChanged;
        _session.Changed += OnSessionChanged;
        Loc.Manager.LanguageChanged += OnLanguageChanged;
        SyncWithNavigation();
    }

    public string DisclaimerText => AppText.MedicalDisclaimer;

    public StatusBarViewModel StatusBar { get; }

    public ReadOnlyCollection<NavigationItemViewModel> NavigationItems { get; }

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    public bool IsNavigationVisible => CurrentPage?.ShowsNavigation ?? false;

    /// <summary>Nombre del astronauta con la sesión abierta, para la barra superior.</summary>
    public string CurrentUserName => _session.CurrentUser?.DisplayName ?? string.Empty;

    /// <summary>«Sesión sin conexión» cuando se entró con la cuenta guardada en el equipo; vacío en una sesión en línea.</summary>
    public string SessionModeText => _session.IsOfflineSession ? Loc.T("main.sessionOffline") : string.Empty;

    /// <summary>Interruptor "Enlace con la Tierra": apagado, la app se comporta como sin conexión.</summary>
    public bool IsLinkEnabled
    {
        get => _link.IsLinkEnabled;
        set
        {
            if (_link.IsLinkEnabled == value)
            {
                return;
            }

            _link.IsLinkEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Texto del botón de idioma: el idioma al que se cambiaría («ES» mientras la app está en inglés).</summary>
    public string LanguageButtonText => Loc.T("lang.toggleLabel");

    public string LanguageButtonTip => Loc.T("lang.toggleTip");

    public void Dispose()
    {
        Loc.Manager.LanguageChanged -= OnLanguageChanged;
        _navigation.CurrentPageChanged -= OnCurrentPageChanged;
        _session.Changed -= OnSessionChanged;
    }

    /// <summary>Alterna entre inglés y español; todas las pantallas recalculan sus textos.</summary>
    [RelayCommand]
    private void ToggleLanguage() => Loc.Manager.Toggle();

    [RelayCommand]
    private void Navigate(NavigationItemViewModel item) => _navigation.NavigateTo(item.PageType);

    /// <summary>Abre el panel de información y fuentes de los indicadores.</summary>
    [RelayCommand]
    private void ShowInfo() => _infoPresenter.Show();

    [RelayCommand]
    private void SignOut()
    {
        _auth.SignOut();
        _navigation.NavigateTo<LoginViewModel>();
    }

    private void OnCurrentPageChanged(object? sender, EventArgs e) => SyncWithNavigation();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (NavigationItemViewModel item in NavigationItems)
        {
            item.RefreshLanguage();
        }

        StatusBar.RefreshLanguage();
        OnPropertyChanged(nameof(DisclaimerText));
        OnPropertyChanged(nameof(SessionModeText));
        OnPropertyChanged(nameof(LanguageButtonText));
        OnPropertyChanged(nameof(LanguageButtonTip));

        // Los textos de cada pantalla se calculan al crearla: se recrea, salvo las que guardan lo que el usuario escribe.
        if (CurrentPage is { RebuildsOnLanguageChange: false } page)
        {
            page.RefreshLanguage();
        }
        else
        {
            _navigation.Reload();
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentUserName));
        OnPropertyChanged(nameof(SessionModeText));
    }

    private void SyncWithNavigation()
    {
        Type? currentType = CurrentPage?.NavigationSection ?? CurrentPage?.GetType();
        foreach (NavigationItemViewModel item in NavigationItems)
        {
            item.IsSelected = item.PageType == currentType;
        }

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsNavigationVisible));
    }
}
