namespace Basarsoft.Api.Settings;

// Strongly-typed view of the "Jwt" section in appsettings.json.
// Bound once in Program.cs and injected where a token is created/validated.
public class JwtSettings
{
    // Who issued the token (this API).
    public string Issuer { get; set; } = string.Empty;

    // Who the token is intended for (the React client).
    public string Audience { get; set; } = string.Empty;

    // Secret signing key. Must be at least 32 characters for HS256.
    // Keep the real value out of source control (use dotnet user-secrets).
    public string Key { get; set; } = string.Empty;

    // How long a freshly issued token stays valid (minutes).
    public int ExpiresMinutes { get; set; }
}
