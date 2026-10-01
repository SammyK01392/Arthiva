using System;
using System.Collections.Generic;
using System.Text;

using System.Text.Json.Serialization;

namespace Arthiva.Models;

public class FirebaseAuthResult
{
    [JsonPropertyName("idToken")]
    public string IdToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("localId")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public string ExpiresIn { get; set; } = "3600";
}

public class FirebaseErrorResponse
{
    [JsonPropertyName("error")]
    public FirebaseErrorDetail? Error { get; set; }
}

public class FirebaseErrorDetail
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}