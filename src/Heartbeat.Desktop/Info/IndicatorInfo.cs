namespace Heartbeat.Desktop.Info;

/// <summary>Una fuente (NASA u otra agencia) citada por un indicador.</summary>
public sealed record InfoSource(string Title, Uri Url);

/// <summary>
/// Lo que el panel de información explica de un indicador: por qué importa, cómo medirlo y qué riesgos tiene.
/// El texto viene tal cual de <c>reference-ranges.json</c> (contrato del backend).
/// </summary>
public sealed record IndicatorInfo(
    string Id,
    string Name,
    string Unit,
    string HazardName,
    string WhyItMatters,
    IReadOnlyList<string> HowToMeasure,
    string Risks,
    IReadOnlyList<InfoSource> Sources);
