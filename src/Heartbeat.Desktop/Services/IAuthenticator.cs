using System.Diagnostics.CodeAnalysis;

namespace Heartbeat.Desktop.Services;

/// <summary>Valida las credenciales del astronauta. El ticket #27 lo sustituye por el login real (online y offline).</summary>
public interface IAuthenticator
{
    bool TryAuthenticate(string userName, string password, [NotNullWhen(true)] out SessionUser? user);
}
