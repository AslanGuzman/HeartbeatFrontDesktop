using System.Windows.Data;
using System.Windows.Markup;

namespace Heartbeat.Desktop.Localization;

/// <summary>
/// Extensión de XAML: <c>Text="{loc:Loc clave}"</c>. Es un enlace al indexador de <see cref="LocalizationManager"/>,
/// así que el texto se actualiza al instante cuando cambia el idioma.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
