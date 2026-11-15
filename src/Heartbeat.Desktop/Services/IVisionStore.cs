using Heartbeat.Shared;

namespace Heartbeat.Desktop.Services;

/// <summary>
/// Lecturas de presión ocular (IOP), agudeza visual y CO₂ del astronauta (HB-FE-04). Se guardan en el equipo y funcionan
/// sin conexión; el análisis de tendencia y correlación lo hace <see cref="VisionAnalysis"/> de Heartbeat.Shared.
/// </summary>
public interface IVisionStore
{
    /// <summary>Lecturas de IOP, agudeza y CO₂ desde <paramref name="from"/>, de la más antigua a la más reciente.</summary>
    IReadOnlyList<EnvironmentReadingRecord> GetReadings(Guid astronautId, DateTimeOffset from);

    /// <summary>Guarda una lectura de <c>iop</c> o <c>visualAcuity</c> tomada ahora.</summary>
    /// <exception cref="ArgumentException">El indicador no es de visión.</exception>
    EnvironmentReadingRecord Add(Guid astronautId, string indicatorId, decimal value);
}
