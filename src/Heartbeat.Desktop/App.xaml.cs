using System.Globalization;
using System.Windows;
using System.Windows.Markup;

using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Heartbeat.Desktop;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // Por defecto WPF formatea fechas y números del XAML con en-US; se usa la cultura del usuario.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(Environment.GetCommandLineArgs().Skip(1).ToArray());
        builder.Services.AddHeartbeatDesktop();
        _host = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        await _host.StartAsync();

        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        this.MainWindow = window;

        // El login es siempre la primera pantalla.
        _host.Services.GetRequiredService<INavigationService>().NavigateTo<LoginViewModel>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Task.Run evita bloquear el hilo de la interfaz esperando una continuación que necesite ese mismo hilo.
        Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
        _host.Dispose();

        base.OnExit(e);
    }
}
