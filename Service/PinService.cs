using System.Security.Cryptography;

namespace Arthiva.Services;

/// <summary>
/// PBKDF2-based (Rfc2898DeriveBytes) salted hashing for the local 4-digit
/// App PIN. Deliberately slow (high iteration count) so a stolen SQLite
/// file can't be brute-forced quickly, while still being instant for a
/// single verify on a phone. Uses only built-in .NET cryptographic APIs.
/// </summary>
public class PinService : IPinService
{
    private const int SaltSizeBytes = 16;   // 128-bit random salt
    private const int HashSizeBytes = 32;   // 256-bit derived key
    private const int Iterations = 150_000; // slow on purpose
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashPin(string pin)
    {
        ValidatePinFormat(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, Algorithm, HashSizeBytes);

        // Stored as: iterations.base64(salt).base64(hash)
        // Storing the iteration count lets us safely raise it later without
        // breaking verification of PINs hashed under the old value.
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPin(string pin, string storedHash)
    {
        if (string.IsNullOrEmpty(pin) || pin.Length != 4 || !IsAllDigits(pin))
            return false;

        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, Algorithm, expectedHash.Length);

        // Constant-time comparison to avoid leaking timing information.
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static void ValidatePinFormat(string pin)
    {
        if (string.IsNullOrEmpty(pin) || pin.Length != 4 || !IsAllDigits(pin))
            throw new ArgumentException("PIN must be exactly 4 numeric digits.", nameof(pin));
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }
        return true;
    }
}
