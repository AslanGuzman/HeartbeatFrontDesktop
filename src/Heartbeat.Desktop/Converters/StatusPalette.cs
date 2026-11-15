using System.Windows.Media;

using Heartbeat.Shared;
using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.Converters;

/// <summary>
/// Semáforo de estados de salud (NR-01). Es la única fuente de estos colores.
/// <list type="bullet">
/// <item><c>Dot</c>: punto, ondas y relleno decorativo.</item>
/// <item><c>Text</c>: texto del chip, más oscuro que <c>Dot</c> para tener contraste suficiente sobre <c>Soft</c>.</item>
/// <item><c>Soft</c>: fondo del chip.</item>
/// </list>
/// El rojo crítico es el rojo de marca (#990E0E).
/// </summary>
public static class StatusPalette
{
    public static Brush Dot(HealthStatus? status) => status switch
    {
        HealthStatus.Normal => NormalDot,
        HealthStatus.Watch => WatchDot,
        HealthStatus.Critical => CriticalDot,
        _ => NoneDot,
    };

    public static Brush Text(HealthStatus? status) => status switch
    {
        HealthStatus.Normal => NormalText,
        HealthStatus.Watch => WatchText,
        HealthStatus.Critical => CriticalText,
        _ => NoneText,
    };

    public static Brush Soft(HealthStatus? status) => status switch
    {
        HealthStatus.Normal => NormalSoft,
        HealthStatus.Watch => WatchSoft,
        HealthStatus.Critical => CriticalSoft,
        _ => NoneSoft,
    };

    /// <summary>Nombre del estado en el idioma activo, para mostrarlo siempre junto al color.</summary>
    public static string Name(HealthStatus? status) => status switch
    {
        HealthStatus.Normal => Loc.T("status.normal"),
        HealthStatus.Watch => Loc.T("status.watch"),
        HealthStatus.Critical => Loc.T("status.critical"),
        _ => Loc.T("status.noData"),
    };

    private static readonly Brush NormalDot = Frozen(0x2E, 0x9E, 0x5B);
    private static readonly Brush WatchDot = Frozen(0xD9, 0x8E, 0x04);
    private static readonly Brush CriticalDot = Frozen(0x99, 0x0E, 0x0E);
    private static readonly Brush NoneDot = Frozen(0x9A, 0x9E, 0xB5);

    private static readonly Brush NormalText = Frozen(0x1B, 0x6B, 0x3C);
    private static readonly Brush WatchText = Frozen(0x80, 0x52, 0x00);
    private static readonly Brush CriticalText = Frozen(0x99, 0x0E, 0x0E);
    private static readonly Brush NoneText = Frozen(0x6A, 0x6E, 0x86);

    private static readonly Brush NormalSoft = Frozen(0xE6, 0xF3, 0xEB);
    private static readonly Brush WatchSoft = Frozen(0xFB, 0xF2, 0xDE);
    private static readonly Brush CriticalSoft = Frozen(0xF8, 0xEA, 0xEA);
    private static readonly Brush NoneSoft = Frozen(0xEE, 0xED, 0xF8);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
