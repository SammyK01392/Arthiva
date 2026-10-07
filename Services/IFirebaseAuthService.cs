using MoneySpend.Models;

namespace MoneySpend.Services;

// Replace the file that currently declares IFirebaseAuthService with this one.
// Every member of the old interface is kept; GetValidIdTokenAsync gained an
// optional parameter, and GetUidAsync / SignedIn / SignedOut are new.
public interface IFirebaseAuthService
{
    /// <summary>Raised after a successful sign-up or sign-in.</summary>
    event EventHandler? SignedIn;

    /// <summary>Raised after local session data has been cleared.</summary>
    event EventHandler? SignedOut;

    Task<FirebaseAuthResult> SignUpAsync(string email, string password);
    Task<FirebaseAuthResult> SignInAsync(string email, string password);
    Task SendPasswordResetAsync(string email);

    /// <summary>
    /// Returns a valid ID token, refreshing if needed (serialized by a lock).
    /// null = not logged in / session no longer valid.
    /// Throws HttpRequestException on transient refresh failures (offline, 5xx).
    /// </summary>
    Task<string?> GetValidIdTokenAsync(bool forceRefresh = false);

    Task<string?> GetUidAsync();
    Task<bool> IsLoggedInAsync();
    Task SignOutAsync();
}
