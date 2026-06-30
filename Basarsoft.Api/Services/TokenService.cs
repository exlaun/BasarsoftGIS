using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Basarsoft.Api.Settings;
using Microsoft.IdentityModel.Tokens;

namespace Basarsoft.Api.Services;

public class TokenService : ITokenService
{
    private readonly JwtSettings _settings;

    public TokenService(JwtSettings settings)
    {
        _settings = settings;
    }

    public AuthResponse CreateToken(User user)
    {
        // Short-lived token: the client is told exactly when it dies so it can self-logout.
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.ExpiresMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new AuthResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            Username = user.Username,
            ExpiresAt = expiresAt
        };
    }
}
