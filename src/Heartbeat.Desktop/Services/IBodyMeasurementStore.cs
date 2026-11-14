namespace Heartbeat.Desktop.Services;

/// <summary>Zonas del cuerpo que se miden. En microgravedad la columna se alarga y las piernas pierden volumen, por eso se vigilan.</summary>
public enum BodyRegion
{
    Height,
    Neck,
    Chest,
    Waist,
    Thigh,
    Calf,
}

/// <summary>Una medida corporal en centímetros.</summary>
public sealed record BodyMeasurement(BodyRegion Region, decimal Centimeters, DateTimeOffset RecordedAt);

/// <summary>
/// Medidas corporales del astronauta. Provisional: la base local (#8) y la sincronización (#32) la sustituirán;
/// el contrato del backend todavía no define estas medidas.
/// </summary>
public interface IBodyMeasurementStore
{
    /// <summary>Historial de una zona, de la medida más reciente a la más antigua.</summary>
    IReadOnlyList<BodyMeasurement> GetHistory(string userName, BodyRegion region);

    /// <summary>Guarda las medidas indicadas como una sola sesión (mismo instante).</summary>
    void Add(string userName, IReadOnlyList<(BodyRegion Region, decimal Centimeters)> values);
}
