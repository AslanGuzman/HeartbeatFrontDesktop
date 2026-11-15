using Microsoft.Extensions.Configuration;

namespace Heartbeat.Desktop.Services.Auth;

/// <summary>
/// Cómo habla la app con la Crew API. Se lee de <c>appsettings.json</c> (sección <c>CrewApi</c>) y se puede
/// sobrescribir con variables de entorno, por ejemplo <c>CrewApi__Mode=Live</c>.
/// </summary>
public sealed record CrewApiOptions(string Mode, string BaseUrl, int TimeoutSeconds)
{
    public const string DemoMode = "Demo";
    public const string LiveMode = "Live";

    /// <summary>Modo demostración: tres astronautas ficticios, sin servidor.</summary>
    public bool IsDemo => !string.Equals(Mode, LiveMode, StringComparison.OrdinalIgnoreCase);

    public static CrewApiOptions From(IConfiguration? configuration)
    {
        IConfigurationSection section = configuration?.GetSection("CrewApi") ?? new ConfigurationBuilder().Build().GetSection("CrewApi");
        string mode = section["Mode"] is { Length: > 0 } m ? m : DemoMode;
        string baseUrl = section["BaseUrl"] is { Length: > 0 } u ? u : "http://localhost:5027/";
        int timeout = int.TryParse(section["TimeoutSeconds"], out int t) && t > 0 ? t : 8;
        return new CrewApiOptions(mode, baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", timeout);
    }
}
