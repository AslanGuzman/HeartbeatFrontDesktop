using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Heartbeat.Desktop.Localization;

/// <summary>Idiomas de la interfaz. El inglés es el idioma por defecto.</summary>
public enum AppLanguage
{
    English,
    Spanish,
}

/// <summary>
/// Idioma activo de la aplicación y acceso a los textos. Es un singleton porque lo leen tanto el XAML
/// (<see cref="LocExtension"/>) como código estático (por ejemplo <c>StatusPalette.Name</c>).
/// Notifica con <see cref="LanguageChanged"/> y con el indexador, de modo que los enlaces de XAML se refrescan solos.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private AppLanguage _language = AppLanguage.English;

    private LocalizationManager()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se produce después de cambiar el idioma. Los ViewModels lo usan para recalcular sus textos.</summary>
    public event EventHandler? LanguageChanged;

    public AppLanguage Language => _language;

    /// <summary>Cultura para dar formato a fechas con nombre de día o mes; no se usa para leer números escritos por el usuario.</summary>
    public CultureInfo Culture => _language == AppLanguage.Spanish ? SpanishCulture : EnglishCulture;

    private static CultureInfo EnglishCulture { get; } = CultureInfo.GetCultureInfo("en-US");

    private static CultureInfo SpanishCulture { get; } = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Texto en el idioma activo; si la clave no existe devuelve la propia clave (se nota a simple vista).</summary>
    public string this[string key] => Strings.Get(key, _language);

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle() => SetLanguage(_language == AppLanguage.English ? AppLanguage.Spanish : AppLanguage.English);
}

/// <summary>Acceso corto a los textos desde código: <c>Loc.T("clave")</c>.</summary>
public static class Loc
{
    public static LocalizationManager Manager => LocalizationManager.Instance;

    public static AppLanguage Language => Manager.Language;

    public static CultureInfo Culture => Manager.Culture;

    public static string T(string key) => Manager[key];

    /// <summary>Texto con huecos <c>{0}</c>, <c>{1}</c>… rellenados con la cultura del idioma activo.</summary>
    public static string T(string key, params object?[] args) => string.Format(Culture, Manager[key], args);
}

/// <summary>Guarda el idioma elegido en un fichero junto a la base local, para recordarlo entre sesiones.</summary>
public static class LanguagePreference
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Heartbeat",
        "language.txt");

    public static AppLanguage Load(string? path = null)
    {
        try
        {
            string text = File.ReadAllText(path ?? DefaultPath).Trim();
            return text.Equals("es", StringComparison.OrdinalIgnoreCase) ? AppLanguage.Spanish : AppLanguage.English;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppLanguage.English;
        }
    }

    public static void Save(AppLanguage language, string? path = null)
    {
        try
        {
            string target = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, language == AppLanguage.Spanish ? "es" : "en");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recordar el idioma es una comodidad: si no se puede escribir, se vuelve al inglés en el próximo arranque.
        }
    }
}
