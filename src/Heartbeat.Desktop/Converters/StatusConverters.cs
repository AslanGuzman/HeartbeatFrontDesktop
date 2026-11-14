using System.Globalization;
using System.Windows;
using System.Windows.Data;

using Heartbeat.Shared;

namespace Heartbeat.Desktop.Converters;

/// <summary>
/// <see cref="HealthStatus"/> (o <c>null</c> = sin datos) a pincel del semáforo. El parámetro elige la variante:
/// <c>Dot</c> (por defecto), <c>Text</c> o <c>Soft</c>.
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        HealthStatus? status = value as HealthStatus?;
        string? variant = parameter as string;
        return variant switch
        {
            "Text" => StatusPalette.Text(status),
            "Soft" => StatusPalette.Soft(status),
            _ => StatusPalette.Dot(status),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Muestra un elemento solo cuando su texto no está vacío (errores, avisos).</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible cuando el valor es <c>false</c> (estados vacíos).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
