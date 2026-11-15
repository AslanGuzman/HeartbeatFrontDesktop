using System.Security.Cryptography;
using System.Text;

namespace Heartbeat.Desktop.Services.Auth;

/// <summary>Protege los tokens antes de guardarlos en la base local (CONTRACTS.md §3: <c>TokenCache</c>).</summary>
public interface ITokenProtector
{
    string Protect(string plainText);

    /// <summary>Devuelve el texto original, o <c>null</c> si no se puede descifrar (otro usuario de Windows, dato dañado).</summary>
    string? Unprotect(string protectedText);
}

/// <summary>DPAPI con alcance de usuario actual: solo la misma cuenta de Windows en este equipo puede leer los tokens.</summary>
public sealed class DpapiTokenProtector : ITokenProtector
{
    // Entropía propia de la aplicación: otro programa del mismo usuario no descifra estos datos por accidente.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Heartbeat.Desktop.TokenCache.v1");

    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        byte[] blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(blob);
    }

    public string? Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return null;
        }

        try
        {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
