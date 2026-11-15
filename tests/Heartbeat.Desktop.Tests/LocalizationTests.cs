using System.IO;
using System.Text.RegularExpressions;

using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.Navigation;
using Heartbeat.Desktop.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Tests;

/// <summary>La tabla de textos: completa, coherente entre idiomas y sin claves que falten en el código ni en las vistas.</summary>
public sealed partial class LocalizationTableTests
{
    [GeneratedRegex(@"\{\d+(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string Slots(string text) =>
        string.Join(",", Placeholder().Matches(text).Select(m => m.Value.Split(':')[0].Trim('{', '}')).Distinct().Order());

    [Fact]
    public void Every_key_has_an_english_and_a_spanish_text()
    {
        foreach ((string key, (string en, string es)) in Strings.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(en), $"{key}: falta el texto en inglés");
            Assert.False(string.IsNullOrWhiteSpace(es), $"{key}: falta el texto en español");
        }
    }

    [Fact]
    public void Both_languages_use_the_same_placeholders()
    {
        foreach ((string key, (string en, string es)) in Strings.All)
        {
            Assert.True(Slots(en) == Slots(es), $"{key}: los huecos {{n}} difieren entre inglés y español");
        }
    }

    [Fact]
    public void Texts_that_must_differ_between_languages_do()
    {
        // Una traducción que se quedó copiada del otro idioma suele ser un olvido (se permiten nombres propios y símbolos).
        string[] same = ["Dashboard", "Check-up", "Normal", "Abdomen"];
        List<string> untranslated = Strings.All
            .Where(p => p.Value.En == p.Value.Es && p.Value.En.Length > 12 && !same.Contains(p.Value.En) && !p.Key.StartsWith("report.", StringComparison.Ordinal))
            .Select(p => p.Key)
            .ToList();

        Assert.True(untranslated.Count == 0, "Sin traducir: " + string.Join(", ", untranslated));
    }

    [Fact]
    public void A_missing_key_shows_the_key_itself()
    {
        Assert.Equal("no.such.key", Strings.Get("no.such.key", AppLanguage.English));
    }

    [Fact]
    public void Every_key_used_in_the_source_exists_in_the_table()
    {
        string root = FindSourceRoot();
        Regex code = new("""Loc\.T\(\s*"([^"]+)"|\bLoc\.T\(\s*\r?\n?\s*"([^"]+)"|NavigationItemViewModel\("([^"]+)"|\("(?:head|heart|abdomen)", "([^"]+)"|T\("([a-z]+\.[A-Za-z.0-9]+)"\)""");
        Regex xaml = new(@"\{loc:Loc ([^}\s]+)\}");
        List<string> missing = [];

        foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}")))
        {
            string text = File.ReadAllText(file);
            IEnumerable<string> keys = file.EndsWith(".xaml")
                ? xaml.Matches(text).Select(m => m.Groups[1].Value)
                : code.Matches(text).SelectMany(m => m.Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => g.Value));
            foreach (string key in keys)
            {
                if (!Strings.All.ContainsKey(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Claves sin definir: " + string.Join("; ", missing));
    }

    [Fact]
    public void Indicator_and_hazard_names_exist_for_every_contract_id()
    {
        Heartbeat.Desktop.Info.ReferenceRangesInfoProvider provider = new();
        foreach (Heartbeat.Desktop.Info.IndicatorInfo info in provider.Indicators)
        {
            Assert.True(Strings.All.ContainsKey($"ind.{info.Id}"), $"falta ind.{info.Id}");
        }

        foreach (string hazard in new[] { "spaceRadiation", "isolationAndConfinement", "distanceFromEarth", "gravityFields", "hostileClosedEnvironment" })
        {
            Assert.True(Strings.All.ContainsKey($"hazard.{hazard}"), $"falta hazard.{hazard}");
        }
    }

    private static string FindSourceRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HeartbeatDesktop.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Heartbeat.Desktop");
    }
}

/// <summary>La preferencia de idioma: inglés por defecto y se recuerda lo elegido.</summary>
public sealed class LanguagePreferenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "heartbeat-lang-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void English_is_the_default_when_nothing_was_saved()
    {
        Assert.Equal(AppLanguage.English, LanguagePreference.Load(Path.Combine(_dir, "language.txt")));
    }

    [Theory]
    [InlineData(AppLanguage.Spanish)]
    [InlineData(AppLanguage.English)]
    public void The_chosen_language_is_remembered(AppLanguage language)
    {
        string path = Path.Combine(_dir, "language.txt");

        LanguagePreference.Save(language, path);

        Assert.Equal(language, LanguagePreference.Load(path));
    }
}

/// <summary>Cambiar el idioma en la ventana: textos, menú y pantalla actual (en una colección que no corre en paralelo).</summary>
[Collection(LanguageCollection.Name)]
public sealed class LanguageSwitchTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly INavigationService _navigation;
    private readonly MainWindowViewModel _window;

    public LanguageSwitchTests()
    {
        ServiceCollection services = TestContainer.Services(out _);
        services.AddSingleton<Heartbeat.Desktop.Services.IConnectivityService>(new FakeConnectivityService());
        _provider = services.BuildServiceProvider();
        _navigation = _provider.GetRequiredService<INavigationService>();
        _window = _provider.GetRequiredService<MainWindowViewModel>();
    }

    public void Dispose()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.Spanish);
        _provider.Dispose();
    }

    [Fact]
    public void Toggling_flips_between_english_and_spanish()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);

        _window.ToggleLanguageCommand.Execute(null);
        Assert.Equal(AppLanguage.Spanish, Loc.Language);

        _window.ToggleLanguageCommand.Execute(null);
        Assert.Equal(AppLanguage.English, Loc.Language);
    }

    [Fact]
    public void The_menu_and_the_disclaimer_follow_the_language()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);

        Assert.Equal(["Dashboard", "Check-up", "History", "Messages", "Radiation", "Vision", "Profile"], _window.NavigationItems.Select(i => i.Title));
        Assert.Equal("Self-assessment tool, not a medical diagnosis", _window.DisclaimerText);
        Assert.Equal("ES", _window.LanguageButtonText);

        _window.ToggleLanguageCommand.Execute(null);

        Assert.Equal(["Dashboard", "Check-up", "Historial", "Mensajes", "Radiación", "Visión", "Perfil"], _window.NavigationItems.Select(i => i.Title));
        Assert.Equal("Herramienta de autoevaluación, no diagnóstico médico", _window.DisclaimerText);
        Assert.Equal("EN", _window.LanguageButtonText);
    }

    [Fact]
    public void The_language_notifies_the_menu_so_the_bindings_refresh()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);
        List<string?> changed = [];
        _window.NavigationItems[2].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _window.ToggleLanguageCommand.Execute(null);

        Assert.Contains(nameof(NavigationItemViewModel.Title), changed);
    }

    [Fact]
    public async Task The_current_page_is_rebuilt_in_the_new_language()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);
        LoginViewModel login = _navigation.NavigateTo<LoginViewModel>();
        login.Username = "ana";
        await login.SignInCommand.ExecuteAsync("demo123");
        DashboardViewModel before = Assert.IsType<DashboardViewModel>(_window.CurrentPage);
        Assert.StartsWith("Hello", before.Greeting);

        _window.ToggleLanguageCommand.Execute(null);

        DashboardViewModel after = Assert.IsType<DashboardViewModel>(_window.CurrentPage);
        Assert.NotSame(before, after);
        Assert.StartsWith("Hola", after.Greeting);
    }

    [Fact]
    public void The_login_keeps_what_was_typed_when_the_language_changes()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);
        LoginViewModel login = _navigation.NavigateTo<LoginViewModel>();
        login.Username = "ana";
        Assert.Equal("Sign in", login.SignInText);

        _window.ToggleLanguageCommand.Execute(null);

        Assert.Same(login, _window.CurrentPage);
        Assert.Equal("ana", login.Username);
        Assert.Equal("Entrar", login.SignInText);
    }

    [Fact]
    public void Status_names_and_indicator_names_follow_the_language()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);
        Heartbeat.Desktop.Info.ReferenceRangesInfoProvider provider = new();

        Assert.Equal("Watch", Heartbeat.Desktop.Converters.StatusPalette.Name(Heartbeat.Shared.HealthStatus.Watch));
        Assert.Equal("Heart rate", provider.Find("heartRate")!.Name);
        string[] english = ["Space radiation", "Isolation and confinement", "Distance from Earth", "Gravity fields", "Hostile and closed environments"];
        Assert.All(provider.Indicators, i => Assert.Contains(i.HazardName, english));

        LocalizationManager.Instance.SetLanguage(AppLanguage.Spanish);

        Assert.Equal("Vigilancia", Heartbeat.Desktop.Converters.StatusPalette.Name(Heartbeat.Shared.HealthStatus.Watch));
        Assert.Equal("Frecuencia cardíaca", provider.Find("heartRate")!.Name);
        string[] spanish = ["Radiación espacial", "Aislamiento y confinamiento", "Distancia de la Tierra", "Campos de gravedad", "Entorno cerrado y hostil"];
        Assert.All(provider.Indicators, i => Assert.Contains(i.HazardName, spanish));
    }
}
