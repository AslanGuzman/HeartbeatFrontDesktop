using System.IO;
using System.Text.Json;

using Heartbeat.Desktop.Data;
using Heartbeat.Desktop.Info;
using Heartbeat.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Heartbeat.Desktop.Services.Sync;

/// <summary>
/// Los rangos de referencia con los que arranca la app: los que descargó el servidor en una sincronización anterior
/// (<c>RangesCache</c>) si son más nuevos y válidos, y si no los embebidos en <c>Heartbeat.Shared</c>. Unos rangos
/// descargados durante la sesión no cambian el evaluador en marcha: se aplican al reiniciar.
/// </summary>
public sealed class RangesSource
{
    public RangesSource(string json, string inUseVersion)
    {
        Json = json;
        InUseVersion = inUseVersion;
        LatestVersion = inUseVersion;
    }

    /// <summary>JSON con el que trabajan el evaluador y las fichas de información.</summary>
    public string Json { get; }

    /// <summary>Versión de <see cref="Json"/>.</summary>
    public string InUseVersion { get; }

    /// <summary>La versión más reciente que tiene el equipo, en uso o ya descargada (esperando a reiniciar). Es la que se informa al servidor.</summary>
    public string LatestVersion { get; private set; }

    /// <summary>Hay unos rangos descargados que aún no se están usando.</summary>
    public bool RestartPending => !string.Equals(LatestVersion, InUseVersion, StringComparison.Ordinal);

    public void MarkDownloaded(string version) => LatestVersion = version;

    /// <summary>Lee la caché; ante cualquier problema (sin tablas, JSON dañado, rangos viejos) usa los embebidos.</summary>
    public static RangesSource Load(IServiceProvider services)
    {
        string embeddedJson = ReferenceRanges.Json;
        string embeddedVersion = ReferenceRanges.Version;

        try
        {
            using IServiceScope scope = services.CreateScope();
            LocalDbContext db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            RangesCache? cached = db.RangesCache.FirstOrDefault();
            if (cached is not null
                && string.CompareOrdinal(cached.Version, embeddedVersion) > 0
                && TryValidate(cached.Json, out string? version)
                && version == cached.Version)
            {
                return new RangesSource(cached.Json, cached.Version);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Sin base inicializada o dañada: los rangos embebidos siempre funcionan.
        }

        return new RangesSource(embeddedJson, embeddedVersion);
    }

    /// <summary>Comprueba que el JSON sirve para evaluar y para las fichas; devuelve su versión.</summary>
    public static bool TryValidate(string json, out string? version)
    {
        version = null;
        try
        {
            HealthEvaluator evaluator = new(json);
            ReferenceRangesInfoProvider info = new(json);
            version = evaluator.RangesVersion;
            return !string.IsNullOrWhiteSpace(version) && version == info.RangesVersion && info.Indicators.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }
}
