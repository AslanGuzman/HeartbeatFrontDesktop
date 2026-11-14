namespace Heartbeat.Desktop.Info;

/// <summary>Contenido del panel de información: una ficha por indicador, con sus fuentes.</summary>
public interface IIndicatorInfoProvider
{
    /// <summary>Versión del fichero de rangos del que sale el contenido.</summary>
    string RangesVersion { get; }

    /// <summary>Indicadores en el orden del contrato.</summary>
    IReadOnlyList<IndicatorInfo> Indicators { get; }

    /// <summary>Devuelve la ficha del indicador, o <c>null</c> si el id no existe.</summary>
    IndicatorInfo? Find(string id);
}
