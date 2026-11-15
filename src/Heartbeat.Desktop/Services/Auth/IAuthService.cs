using Heartbeat.Desktop.Localization;

namespace Heartbeat.Desktop.Services.Auth;

public enum SignInFailure
{
    None,

    /// <summary>Falta el usuario o la contraseña.</summary>
    MissingFields,

    /// <summary>El servidor (en línea) o el equipo (sin conexión) rechazó las credenciales.</summary>
    InvalidCredentials,

    /// <summary>Sin conexión y sin una cuenta guardada en este equipo: el primer acceso debe ser en línea.</summary>
    FirstSignInNeedsConnection,

    /// <summary>Había red pero el servidor no respondió, y la cuenta no existe en este equipo.</summary>
    ServerUnavailable,
}

/// <summary>Resultado de un intento de inicio de sesión.</summary>
public sealed record SignInResult(SessionUser? User, bool IsOffline, SignInFailure Failure)
{
    public bool Succeeded => User is not null;

    public static SignInResult Success(SessionUser user, bool offline) => new(user, offline, SignInFailure.None);

    public static SignInResult Fail(SignInFailure failure) => new(null, false, failure);

    /// <summary>Mensaje para el astronauta, en el idioma activo.</summary>
    public string? Message => Failure switch
    {
        SignInFailure.MissingFields => Loc.T("auth.missing"),
        SignInFailure.InvalidCredentials => Loc.T("auth.invalid"),
        SignInFailure.FirstSignInNeedsConnection => Loc.T("auth.firstOnline"),
        SignInFailure.ServerUnavailable => Loc.T("auth.server"),
        _ => null,
    };
}

/// <summary>
/// Inicio y cierre de sesión del astronauta, también sin conexión (MR-12). Con conexión valida contra la Crew API y
/// guarda un hash local; sin conexión valida contra ese hash.
/// </summary>
public interface IAuthService
{
    Task<SignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>Cierra la sesión y borra los tokens guardados de este astronauta. La cuenta local se conserva para poder volver a entrar sin conexión.</summary>
    void SignOut();
}
