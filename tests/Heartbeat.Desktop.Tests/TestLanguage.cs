using System.Runtime.CompilerServices;

using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.Tests;

/// <summary>
/// La app arranca en inglés, pero casi todas las pruebas comprueban textos en español: se fija el idioma una sola vez
/// al cargar el ensamblado. Las pruebas que cambian el idioma van en la colección <see cref="LanguageCollection"/>,
/// que no corre en paralelo con el resto, y lo dejan otra vez en español al terminar.
/// </summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Pin() => LocalizationManager.Instance.SetLanguage(AppLanguage.Spanish);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Language";
}
