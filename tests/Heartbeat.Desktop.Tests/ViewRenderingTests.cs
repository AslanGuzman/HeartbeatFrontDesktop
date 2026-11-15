using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Heartbeat.Desktop.Converters;
using Heartbeat.Desktop.Demo;
using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.ViewModels;
using Heartbeat.Desktop.Views;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>Un hilo STA con un <see cref="Application"/> que carga los estilos de la app, para montar vistas reales en las pruebas.</summary>
internal static class WpfHost
{
    private static readonly Dispatcher UiDispatcher = Start();

    public static T Run<T>(Func<T> work) => UiDispatcher.Invoke(work);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using ManualResetEventSlim ready = new();
        Thread thread = new(() =>
        {
            Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string theme in new[] { "Themes/Colors.xaml", "Themes/Controls.xaml", "Views/PageTemplates.xaml" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Heartbeat.Desktop;component/{theme}"),
                });
            }

            app.Resources["StatusToBrush"] = new StatusToBrushConverter();
            app.Resources["NonEmptyToVisibility"] = new NonEmptyToVisibilityConverter();
            app.Resources["InverseBoolToVisibility"] = new InverseBooleanToVisibilityConverter();
            app.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();

            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test host",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}

/// <summary>
/// Monta la ventana real con cada pantalla y comprueba, en inglés y en español, que no queda ninguna clave sin traducir
/// ni ningún texto del otro idioma, y que cambiar el idioma con la ventana abierta actualiza lo que se ve.
/// </summary>
[Collection(LanguageCollection.Name)]
public sealed partial class ViewRenderingTests : IDisposable
{
    [GeneratedRegex(@"^[a-z]+(\.[A-Za-z0-9]+)+$")]
    private static partial Regex LooksLikeAKey();

    public void Dispose() => LocalizationManager.Instance.SetLanguage(AppLanguage.Spanish);

    [Fact]
    public async Task Every_screen_shows_text_in_the_active_language_and_follows_the_switch()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);

        ServiceCollection services = TestContainer.Services(out _);
        services.AddSingleton<Heartbeat.Desktop.Services.IConnectivityService>(new FakeConnectivityService());
        await using ServiceProvider provider = services.BuildServiceProvider();
        DemoDataSeeder.Seed(provider);
        INavigationService navigation = provider.GetRequiredService<INavigationService>();
        MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();

        LoginViewModel login = navigation.NavigateTo<LoginViewModel>();
        login.Username = "ana";
        await login.SignInCommand.ExecuteAsync("demo123");

        Type[] pages =
        [
            typeof(LoginViewModel), typeof(DashboardViewModel), typeof(CheckUpViewModel), typeof(HistoryViewModel),
            typeof(MessagesViewModel), typeof(RadiationViewModel), typeof(ProfileViewModel),
        ];

        FrameworkElement root = WpfHost.Run(() =>
        {
            MainWindow window = provider.GetRequiredService<MainWindow>();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEARTBEAT_RENDER_DIR")))
            {
                // Solo para capturar PNG: sin mostrarse, WPF no dispara Loaded y la animación de entrada no arranca.
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = window.Top = -32000;
                window.ShowInTaskbar = false;
                window.Show();
            }

            return (FrameworkElement)window.Content;
        });

        // ---- Inglés ----
        foreach (Type page in pages)
        {
            List<string> texts = Show(root, navigation, page);
            AssertLanguage(texts, AppLanguage.English, page);
            Assert.Contains("Link to Earth", texts);
            Assert.Contains("ES", texts);
            RenderIfRequested(root, $"en-{page.Name}");
        }

        // ---- El mismo recorrido tras cambiar de idioma con la ventana abierta ----
        WpfHost.Run(() => { navigation.NavigateTo(typeof(DashboardViewModel)); return 0; });
        WpfHost.Run(() => { viewModel.ToggleLanguageCommand.Execute(null); return 0; });

        foreach (Type page in pages)
        {
            List<string> texts = Show(root, navigation, page);
            AssertLanguage(texts, AppLanguage.Spanish, page);
            Assert.Contains("Enlace con la Tierra", texts);
            Assert.Contains("EN", texts);
            RenderIfRequested(root, $"es-{page.Name}");
        }

        // La pantalla del resultado de un check-up (se abre al guardar uno).
        Heartbeat.Desktop.Services.ISessionContext session = provider.GetRequiredService<Heartbeat.Desktop.Services.ISessionContext>();
        var store = provider.GetRequiredService<Heartbeat.Desktop.Services.IHealthRecordStore>();
        var record = store.GetCheckUps(session.CurrentUser!.Id)[0];
        WpfHost.Run(() => navigation.NavigateTo<CheckUpResultViewModel>(result => result.Load(record)));
        List<string> resultTexts = Show(root, navigation, typeof(CheckUpResultViewModel));
        AssertLanguage(resultTexts, AppLanguage.Spanish, typeof(CheckUpResultViewModel));

        WpfHost.Run(() => { viewModel.ToggleLanguageCommand.Execute(null); return 0; });
        resultTexts = Show(root, navigation, typeof(CheckUpResultViewModel));
        AssertLanguage(resultTexts, AppLanguage.English, typeof(CheckUpResultViewModel));
        Assert.Contains("Check-up result", resultTexts);
    }

    private static List<string> Show(FrameworkElement root, INavigationService navigation, Type page) => WpfHost.Run(() =>
    {
        if (navigation.CurrentPage?.GetType() != page)
        {
            navigation.NavigateTo(page);
        }

        root.Measure(new Size(1200, 760));
        root.Arrange(new Rect(0, 0, 1200, 760));
        root.UpdateLayout();

        List<string> texts = [];
        Collect(root, texts);
        return texts;
    });

    private static void Collect(DependencyObject node, List<string> texts)
    {
        void Add(string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text.Trim());
            }
        }

        switch (node)
        {
            case TextBlock tb:
                Add(tb.Text);
                break;
            case ContentControl { Content: string s }:
                Add(s);
                break;
        }

        if (node is FrameworkElement fe)
        {
            if (fe.ToolTip is string tip)
            {
                Add(tip);
            }

            Add(AutomationProperties.GetName(fe));
            Add(AutomationProperties.GetHelpText(fe));
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            Collect(VisualTreeHelper.GetChild(node, i), texts);
        }
    }

    private static void AssertLanguage(List<string> texts, AppLanguage language, Type page)
    {
        Assert.NotEmpty(texts);
        Assert.DoesNotContain(texts, t => LooksLikeAKey().IsMatch(t) && !t.Contains(' '));

        // Ningún texto de esta pantalla es la versión del otro idioma (salvo los que se escriben igual en los dos).
        HashSet<string> otherOnly = Strings.All.Values
            .Where(v => v.En != v.Es)
            .Select(v => language == AppLanguage.English ? v.Es : v.En)
            .Where(t => !t.Contains('{') && t.Length > 3)
            .ToHashSet(StringComparer.Ordinal);
        otherOnly.ExceptWith(Strings.All.Values.Select(v => language == AppLanguage.English ? v.En : v.Es));
        string[] leaked = texts.Where(otherOnly.Contains).Distinct().ToArray();
        Assert.True(leaked.Length == 0, $"{page.Name} ({language}): texto del otro idioma: {string.Join(" | ", leaked)}");
    }

    private static void RenderIfRequested(FrameworkElement root, string name)
    {
        string? dir = Environment.GetEnvironmentVariable("HEARTBEAT_RENDER_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        // Las pantallas entran con una animación de aparición: se espera a que termine para que el PNG la muestre completa.
        Thread.Sleep(900);
        WpfHost.Run(() =>
        {
            RenderTargetBitmap bitmap = new(1200, 760, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(dir);
            using FileStream stream = File.Create(Path.Combine(dir, name + ".png"));
            encoder.Save(stream);
            return 0;
        });
    }
}
