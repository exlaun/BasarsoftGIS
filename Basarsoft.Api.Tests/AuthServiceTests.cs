using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using BC = BCrypt.Net.BCrypt;
using Xunit;

namespace Basarsoft.Api.Tests;

public class AuthServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Login_DisabledAccount_ReturnsDisabledStatus()
    {
        await using var db = NewDb();
        db.Users.Add(new User
        {
            Id = 1,
            Username = "disabled-user",
            PasswordHash = BC.HashPassword("correct-password"),
            IsActive = false,
        });
        await db.SaveChangesAsync();

        var result = await new AuthService(db, new StubTokenService()).LoginAsync(new LoginRequest
        {
            Username = "disabled-user",
            Password = "correct-password",
        });

        Assert.Equal(LoginStatus.Disabled, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Login_WrongPassword_RemainsInvalidCredentials()
    {
        await using var db = NewDb();
        db.Users.Add(new User
        {
            Id = 1,
            Username = "active-user",
            PasswordHash = BC.HashPassword("correct-password"),
        });
        await db.SaveChangesAsync();

        var result = await new AuthService(db, new StubTokenService()).LoginAsync(new LoginRequest
        {
            Username = "active-user",
            Password = "wrong-password",
        });

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.Response);
    }

    private sealed class StubTokenService : ITokenService
    {
        public AuthResponse CreateToken(User user) => new()
        {
            Token = "test-token",
            Username = user.Username,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        };
    }
}
