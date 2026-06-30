using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public interface IAuthService
{
    // Returns the auth payload on success, or null if the username is already taken.
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);

    // Returns the auth payload on success, or null if the credentials are invalid.
    Task<AuthResponse?> LoginAsync(LoginRequest request);

    // True if a user with this username exists (step 1 of the forgot-password flow).
    Task<bool> UserExistsAsync(string username);

    // Sets a new (hashed) password for the user. Returns false if the username doesn't exist.
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request);
}
