using System.Diagnostics.CodeAnalysis;

using Heartbeat.Desktop.Services;

namespace Heartbeat.Desktop.Demo;

/// <summary>
/// Acceso de demostración con tres astronautas ficticios. Es provisional: el login real, que valida contra la
/// Crew API y funciona también sin conexión, llega con el ticket #27.
/// </summary>
public sealed class DemoAuthenticator : IAuthenticator
{
    public const string DemoPassword = "demo123";

    public static IReadOnlyList<SessionUser> Accounts { get; } =
    [
        new SessionUser(Guid.Parse("0197c2a0-0000-7000-8000-000000000001"), "ana", "Ana Rivera"),
        new SessionUser(Guid.Parse("0197c2a0-0000-7000-8000-000000000002"), "luis", "Luis Ortega"),
        new SessionUser(Guid.Parse("0197c2a0-0000-7000-8000-000000000003"), "mara", "Mara Sokolova"),
    ];

    public bool TryAuthenticate(string userName, string password, [NotNullWhen(true)] out SessionUser? user)
    {
        string name = (userName ?? string.Empty).Trim();
        user = Accounts.FirstOrDefault(a => string.Equals(a.UserName, name, StringComparison.OrdinalIgnoreCase));

        if (user is null || !string.Equals(password, DemoPassword, StringComparison.Ordinal))
        {
            user = null;
            return false;
        }

        return true;
    }
}
