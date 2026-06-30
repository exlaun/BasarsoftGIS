using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // Creates the first/new user and logs them in.
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        if (result is null)
            return Conflict(new { message = "Username is already taken." });

        return Ok(result);
    }

    // Validates credentials and returns a JWT.
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        if (result is null)
            return Unauthorized(new { message = "Invalid username or password." });

        return Ok(result);
    }

    // Step 1 of the forgot-password flow: confirm the username exists before the client shows
    // the "set a new password" screen. Returns 404 (not 401) on miss so the client's 401 logout
    // interceptor doesn't kick in.
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        if (!await _authService.UserExistsAsync(request.Username))
            return NotFound(new { message = "No account found with that username." });

        return Ok(new { username = request.Username });
    }

    // Step 2: set the new password for a known username.
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(ResetPasswordRequest request)
    {
        if (!await _authService.ResetPasswordAsync(request))
            return NotFound(new { message = "No account found with that username." });

        return Ok(new { message = "Password updated." });
    }

    // Protected endpoint to prove the token gate works (200 with a valid token, 401 without).
    [Authorize]
    [HttpGet("me")]
    public ActionResult Me()
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var username = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName);
        return Ok(new { id, username });
    }
}
