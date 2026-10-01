using System;
using System.Collections.Generic;
using System.Text;

using MoneySpend.Models;

namespace MoneySpend.Services;

public interface IFirebaseAuthService
{
    Task<FirebaseAuthResult> SignUpAsync(string email, string password);
    Task<FirebaseAuthResult> SignInAsync(string email, string password);
    Task<string?> GetValidIdTokenAsync();
    Task SignOutAsync();
    Task<bool> IsLoggedInAsync();
    Task SendPasswordResetAsync(string email);
}