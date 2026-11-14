using CommunityToolkit.Mvvm.ComponentModel;

namespace Heartbeat.Desktop.ViewModels;

/// <summary>Entrada del menú lateral: una pantalla, su etiqueta y su icono (glifo de Segoe MDL2 Assets).</summary>
public sealed partial class NavigationItemViewModel(string title, string glyph, Type pageType) : ObservableObject
{
    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public Type PageType { get; } = pageType;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
