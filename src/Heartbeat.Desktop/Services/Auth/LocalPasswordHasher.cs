using System.Security.Cryptography;

namespace Heartbeat.Desktop.Services.Auth;

/// <summary>
/// Hash PBKDF2 (SHA-256) de la contraseña para iniciar sesión sin conexión (CONTRACTS.md §4). El servidor nunca
/// envía su hash: el escritorio calcula el suyo en el primer acceso en línea. Es un mecanismo de nivel demostración
/// y así se declara.
/// </summary>
public static class LocalPasswordHasher
{
    public const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>Crea sal y hash nuevos. El hash se guarda como «iteraciones.base64» para poder subir las iteraciones más adelante.</summary>
    public static (string Hash, string Salt) Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return ($"{Iterations}.{Convert.ToBase64String(hash)}", Convert.ToBase64String(salt));
    }

    /// <summary>Comprueba la contraseña en tiempo constante. Un hash o una sal con formato inválido nunca coinciden.</summary>
    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
        {
            return false;
        }

        int dot = storedHash.IndexOf('.');
        if (dot <= 0 || !int.TryParse(storedHash.AsSpan(0, dot), out int iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            byte[] expected = Convert.FromBase64String(storedHash[(dot + 1)..]);
            byte[] salt = Convert.FromBase64String(storedSalt);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
