using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Heartbeat.Desktop.Navigation;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Estructura de la ventana: aviso médico, menú lateral, página actual y barra de estado.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public MainWindowViewModel(INavigationService navigation, StatusBarViewModel statusBar)
    {
        _navigation = navigation;
        StatusBar = statusBar;

        NavigationItems =
        [
            new NavigationItemViewModel("Dashboard", "\uE80F", typeof(DashboardViewModel)),
            new NavigationItemViewModel("Check-up", "\uEB51", typeof(CheckUpViewModel)),
            new NavigationItemViewModel("Historial", "\uE81C", typeof(HistoryViewModel)),
            new NavigationItemViewModel("Mensajes", "\uE715", typeof(MessagesViewModel)),
            new NavigationItemViewModel("Perfil", "\uE77B", typeof(ProfileViewModel)),
        ];

        _navigation.CurrentPageChanged += OnCurrentPageChanged;
        SyncWithNavigation();
    }

    public string DisclaimerText => AppText.MedicalDisclaimer;

    public StatusBarViewModel StatusBar { get; }

    public ReadOnlyCollection<NavigationItemViewModel> NavigationItems { get; }

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    public bool IsNavigationVisible => CurrentPage?.ShowsNavigation ?? false;

    [RelayCommand]
    private void Navigate(NavigationItemViewModel item) => _navigation.NavigateTo(item.PageType);

    /// <summary>Vuelve al login. El cierre de sesión real (borrar la sesión activa) llega con el ticket #27.</summary>
    [RelayCommand]
    private void SignOut() => _navigation.NavigateTo<LoginViewModel>();

    private void OnCurrentPageChanged(object? sender, EventArgs e) => SyncWithNavigation();

    private void SyncWithNavigation()
    {
        Type? currentType = CurrentPage?.GetType();
        foreach (NavigationItemViewModel item in NavigationItems)
        {
            item.IsSelected = item.PageType == currentType;
        }

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsNavigationVisible));
    }
}
