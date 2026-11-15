using CommunityToolkit.Mvvm.ComponentModel;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Entrada del menú lateral: una pantalla, su etiqueta (clave de texto) y su icono (glifo de Segoe MDL2 Assets).</summary>
public sealed partial class NavigationItemViewModel(string titleKey, string glyph, Type pageType) : ObservableObject
{
    public string Title => Loc.T(titleKey);

    /// <summary>Vuelve a leer la etiqueta tras cambiar el idioma.</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Title));

    public string Glyph { get; } = glyph;

    public Type PageType { get; } = pageType;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
