using System.Globalization;
using System.Windows;
using System.Windows.Markup;

using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.Services.Auth;
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
        // El inglés es el idioma por defecto; se recuerda el que el usuario eligió con el botón de la barra superior.
        LocalizationManager.Instance.SetLanguage(LanguagePreference.Load());
        LocalizationManager.Instance.LanguageChanged += (_, _) => LanguagePreference.Save(LocalizationManager.Instance.Language);

        // Por defecto WPF formatea fechas y números del XAML con en-US; se usa la cultura del usuario.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // appsettings.json se busca junto al ejecutable, no en la carpeta desde la que se lanzó la app.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = Environment.GetCommandLineArgs().Skip(1).ToArray(),
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddHeartbeatDesktop();
        _host = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // La base debe existir antes de arrancar el host: el motor de sincronización (servicio hospedado) la lee al empezar.
        LocalDatabase.Initialize(_host.Services);

        // El historial de ejemplo solo se siembra en modo demostración.
        if (_host.Services.GetRequiredService<CrewApiOptions>().IsDemo)
        {
            DemoDataSeeder.Seed(_host.Services);
        }

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
