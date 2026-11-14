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
    string TakenAtText,
    string Guidance,
    bool HasData);

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

/// <summary>Un mensaje a control de misión.</summary>
public sealed record MessageRowViewModel(
    string Body,
    string CreatedText,
    string StatusText,
    bool IsSent);
