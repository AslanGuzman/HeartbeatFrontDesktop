using System.IO;

using Heartbeat.Desktop.Services.Auth;

using Microsoft.Extensions.Configuration;

namespace Heartbeat.Desktop.Live;

/// <summary>URL base del backend para las ventanas nuevas: la misma que usa la app (<c>CrewApi:BaseUrl</c>).</summary>
public static class LiveBackend
{
    /// <summary>Lee <c>appsettings.json</c> junto al ejecutable y las variables de entorno, igual que el host de la app.</summary>
    public static string ResolveBaseUrl()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
        return CrewApiOptions.From(configuration).BaseUrl;
    }

    public static Uri ToBaseUri(string baseUrl) => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
}
