namespace Heartbeat.Desktop.Services;

/// <summary>Astronauta con la sesión abierta. <c>Id</c> es el AstronautId del contrato: filtra todos los datos locales (MR-12).</summary>
public sealed record SessionUser(Guid Id, string UserName, string DisplayName)
{
    /// <summary>Primer nombre, para el saludo.</summary>
    public string FirstName => DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? DisplayName;
}

/// <summary>Quién tiene la sesión abierta en este equipo. Todas las consultas de datos se filtran por este usuario (MR-12).</summary>
public interface ISessionContext
{
    SessionUser? CurrentUser { get; }

    /// <summary>La sesión se abrió sin conexión, con la contraseña guardada en el equipo.</summary>
    bool IsOfflineSession { get; }

    event EventHandler? Changed;

    void SignIn(SessionUser user, bool offline = false);

    void SignOut();
}

public sealed class SessionContext : ISessionContext
{
    public SessionUser? CurrentUser { get; private set; }

    public bool IsOfflineSession { get; private set; }

    public event EventHandler? Changed;

    public void SignIn(SessionUser user, bool offline = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        CurrentUser = user;
        IsOfflineSession = offline;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUser = null;
        IsOfflineSession = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
