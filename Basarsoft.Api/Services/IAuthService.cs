using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public interface IAuthService
{
    // Returns the auth payload on success, or null if the username is already taken.
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);

    // Returns a distinct disabled result so the login screen can explain that an administrator must
    // reactivate the account; unknown users and wrong passwords remain InvalidCredentials.
    Task<LoginResult> LoginAsync(LoginRequest request);

    // True if a user with this username exists (step 1 of the forgot-password flow).
    Task<bool> UserExistsAsync(string username);

    // Sets a new (hashed) password for the user. Returns false if the username doesn't exist.
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request);
}
