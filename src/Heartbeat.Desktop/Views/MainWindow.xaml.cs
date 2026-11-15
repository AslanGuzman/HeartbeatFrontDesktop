using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;

using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Assistant;

using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // En pantallas pequeñas (por ejemplo 1366x768) el tamaño por defecto no cabe: se ajusta al área de trabajo
        // para que el pie con el aviso médico nunca quede fuera de la pantalla.
        Rect workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);

        StateChanged += (_, _) => OnStateChanged();
    }

    /// <summary>Enlaza el texto (tooltip y nombre accesible) del botón Maximizar/Restaurar a una clave de idioma.</summary>
    private void BindMaximizeText(string key)
    {
        var binding = new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };
        MaximizeButton.SetBinding(ToolTipProperty, binding);
        MaximizeButton.SetBinding(AutomationProperties.NameProperty, binding);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ===== Galaxy Watch 5 (Live/ y Salud/): ventanas nuevas que se abren desde el rail =====

    private readonly Services.Auth.CrewApiOptions? _liveApiOptions;
    private readonly Heartbeat.Client.ICrewApiClient? _liveApiClient;
    private readonly IAssistantWindowPresenter? _assistantPresenter;

    /// <summary>Constructor que usa la inyección de dependencias: añade la URL base y el token para las ventanas nuevas.</summary>
    public MainWindow(
        MainWindowViewModel viewModel,
        Services.Auth.CrewApiOptions apiOptions,
        Heartbeat.Client.ICrewApiClient apiClient,
        IAssistantWindowPresenter assistantPresenter)
        : this(viewModel)
    {
        _liveApiOptions = apiOptions;
        _liveApiClient = apiClient;
        _assistantPresenter = assistantPresenter;
    }

    private void Assistant_Click(object sender, RoutedEventArgs e) => _assistantPresenter?.Show(this);

    private void FrecuenciaEnVivo_Click(object sender, RoutedEventArgs e)
    {
        var window = _liveApiOptions is null ? new Live.FrecuenciaEnVivoWindow() : new Live.FrecuenciaEnVivoWindow(_liveApiOptions.BaseUrl);
        window.Owner = this;
        window.Show();
    }

    private void ResumenSalud_Click(object sender, RoutedEventArgs e)
    {
        var window = _liveApiOptions is null
            ? new Salud.ResumenSaludWindow()
            : new Salud.ResumenSaludWindow(_liveApiOptions.BaseUrl, () => _liveApiClient?.AccessToken);
        window.Owner = this;
        window.Show();
    }

    // Una ventana sin borde se desborda unos píxeles por cada lado al maximizarla: se compensa con un margen.
    private void OnStateChanged()
    {
        bool maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        BindMaximizeText(maximized ? "main.restore" : "main.maximize");
    }
}
