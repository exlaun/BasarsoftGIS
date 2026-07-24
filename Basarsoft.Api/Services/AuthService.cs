using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using BC = BCrypt.Net.BCrypt;

namespace Basarsoft.Api.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;

    public AuthService(AppDbContext db, ITokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        var usernameTaken = await _db.Users.AnyAsync(u => u.Username == request.Username);
        if (usernameTaken)
            return null;

        var user = new User
        {
            Username = request.Username,
            // Never store the plain password — BCrypt salts + hashes it.
            PasswordHash = BC.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Lost a race with a concurrent registration of the same name; the unique index caught
            // what the pre-check above could not. Same answer as "username taken".
            return null;
        }

        // Auto-login: hand back a token right after registering.
        return _tokenService.CreateToken(user);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        // Soft-deleted users are already filtered out by the global query filter. Keep unknown
        // usernames and wrong passwords indistinguishable, but tell the client when a known account
        // was deliberately disabled by an administrator.
        if (user is null || !BC.Verify(request.Password, user.PasswordHash))
            return LoginResult.InvalidCredentials;

        if (!user.IsActive)
            return LoginResult.Disabled;

        return LoginResult.Ok(_tokenService.CreateToken(user));
    }

    public Task<bool> UserExistsAsync(string username)
        => _db.Users.AnyAsync(u => u.Username == username);

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        if (user is null)
            return false;

        // Same hashing path as register — BCrypt salts + hashes the new password.
        user.PasswordHash = BC.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();
        return true;
    }
}
