namespace Arthiva.Services;

/// <summary>
/// Handles secure hashing/verification of the local 4-digit App PIN.
/// This is NOT an authentication/login system — it only protects local
/// access to the app, similar to a UPI/payment app's app-lock.
/// </summary>
public interface IPinService
{
    /// <summary>
    /// Produces a salted, iterated hash of a 4-digit PIN suitable for local
    /// storage. Never returns or logs the raw PIN.
    /// </summary>
    string HashPin(string pin);

    /// <summary>
    /// Verifies a PIN against a previously stored hash using a constant-time
    /// comparison. Returns false (never throws) for malformed input or hash.
    /// </summary>
    bool VerifyPin(string pin, string storedHash);
}
