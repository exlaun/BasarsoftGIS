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
        await _db.SaveChangesAsync();

        // Auto-login: hand back a token right after registering.
        return _tokenService.CreateToken(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        // Soft-deleted users are already filtered out by the global query filter. A deactivated
        // (is_active = false) account exists but cannot log in.
        if (user is null || !user.IsActive || !BC.Verify(request.Password, user.PasswordHash))
            return null;

        return _tokenService.CreateToken(user);
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
