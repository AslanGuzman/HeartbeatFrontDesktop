namespace Heartbeat.Desktop.Live;

/// <summary>URL base del backend para las llamadas del reloj: la misma que usa la app (<c>CrewApi:BaseUrl</c>).</summary>
public static class LiveBackend
{
    public static Uri ToBaseUri(string baseUrl) => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
}
