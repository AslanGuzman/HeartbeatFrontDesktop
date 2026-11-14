namespace Heartbeat.Desktop.Services;

/// <summary>Astronauta con la sesión abierta.</summary>
public sealed record SessionUser(string UserName, string DisplayName)
{
    /// <summary>Primer nombre, para el saludo.</summary>
    public string FirstName => DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? DisplayName;
}

/// <summary>Quién tiene la sesión abierta en este equipo. Todas las consultas de datos se filtran por este usuario (MR-12).</summary>
public interface ISessionContext
{
    SessionUser? CurrentUser { get; }

    event EventHandler? Changed;

    void SignIn(SessionUser user);

    void SignOut();
}

public sealed class SessionContext : ISessionContext
{
    public SessionUser? CurrentUser { get; private set; }

    public event EventHandler? Changed;

    public void SignIn(SessionUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        CurrentUser = user;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUser = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
