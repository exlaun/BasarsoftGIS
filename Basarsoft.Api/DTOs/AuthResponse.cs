namespace Basarsoft.Api.DTOs;

// Returned by register/login. ExpiresAt (UTC) lets the React client schedule an
// automatic logout exactly when the token dies, with no token-decoding on the client.
public class AuthResponse
{
    public string Token { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}
