using Heartbeat.Shared;

namespace Heartbeat.Desktop.ViewModels;

// Filas inmutables para enlazar a las vistas. Status es null cuando no hay datos todavía.

/// <summary>Tarjeta de un indicador en el dashboard.</summary>
public sealed record IndicatorCardViewModel(
    string Id,
    string Glyph,
    string Name,
    HealthStatus? Status,
    string StatusText,
    string ValueText,
    string UnitText,
    string TakenAtText,
    string Guidance,
    bool HasData)
{
    /// <summary>Si el indicador necesita atención (vigilancia o crítico).</summary>
    public bool NeedsAttention => Status is HealthStatus.Watch or HealthStatus.Critical;
}

/// <summary>Un indicador dentro de una región del cuerpo.</summary>
public sealed record RegionIndicatorViewModel(string Id, string Name, HealthStatus? Status, string StatusText, string ValueText);

/// <summary>
/// Una región de la figura humana (NR-04): su color es el peor estado de los indicadores que le corresponden según
/// <c>bodyRegion</c> del contrato. <c>Geometry</c> es el trazo de la región sobre la figura.
/// </summary>
public sealed record BodyRegionViewModel(
    string Id,
    string Name,
    string Geometry,
    HealthStatus? Status,
    string StatusText,
    string SummaryText,
    string InfoIndicatorId,
    IReadOnlyList<RegionIndicatorViewModel> Indicators);

/// <summary>Una lectura evaluada (resultado de un check-up o detalle del historial).</summary>
public sealed record ReadingRowViewModel(
    string Name,
    string ValueText,
    HealthStatus Status,
    string StatusText,
    string Guidance);

/// <summary>Un check-up del historial con sus lecturas.</summary>
public sealed record CheckUpRowViewModel(
    string DateText,
    HealthStatus Status,
    string StatusText,
    string SyncText,
    bool IsSynced,
    IReadOnlyList<ReadingRowViewModel> Readings);

/// <summary>Un mensaje o reporte que el astronauta escribió a control de misión, con su estado.</summary>
public sealed record OutgoingRowViewModel(
    string Title,
    string Body,
    string WhenText,
    Services.MessageStatus Status,
    string StatusText,
    bool IsReport)
{
    /// <summary>El cuerpo se omite cuando solo repite el asunto (un mensaje de una línea sin asunto propio).</summary>
    public bool ShowBody => !string.Equals(Body.Trim(), Title, StringComparison.Ordinal);
}

/// <summary>Una entrada de la bandeja: una respuesta de control de misión o una alerta que reconocieron.</summary>
public sealed record InboxRowViewModel(
    Guid? MessageId,
    string Title,
    string Body,
    string From,
    string WhenText,
    bool IsAlert,
    bool IsUnread);

/// <summary>Un check-up que se puede adjuntar a un reporte.</summary>
public sealed record CheckUpOptionViewModel(Services.CheckUpRecord CheckUp, string Label, bool AlreadyReported);
