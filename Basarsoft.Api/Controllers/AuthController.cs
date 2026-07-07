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
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IUserAdminService userAdminService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _userAdminService = userAdminService;
        _logger = logger;
    }

    // Logs an unexpected failure and returns a generic 500 — never the exception text (which could
    // leak internal details). Each action calls this from its catch.
    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in AuthController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    // Creates the first/new user and logs them in.
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterAsync(request);
            if (result is null)
                return Conflict(new { message = "Username is already taken." });

            return Ok(result);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Register));
        }
    }

    // Validates credentials and returns a JWT.
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            if (result is null)
                return Unauthorized(new { message = "Invalid username or password." });

            return Ok(result);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Login));
        }
    }

    // Step 1 of the forgot-password flow: confirm the username exists before the client shows
    // the "set a new password" screen. Returns 404 (not 401) on miss so the client's 401 logout
    // interceptor doesn't kick in.
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        try
        {
            if (!await _authService.UserExistsAsync(request.Username))
                return NotFound(new { message = "No account found with that username." });

            return Ok(new { username = request.Username });
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(ForgotPassword));
        }
    }

    // Step 2: set the new password for a known username.
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(ResetPasswordRequest request)
    {
        try
        {
            if (!await _authService.ResetPasswordAsync(request))
                return NotFound(new { message = "No account found with that username." });

            return Ok(new { message = "Password updated." });
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(ResetPassword));
        }
    }

    // Identity + RBAC context for the caller: roles, effective permission names, and whether they may
    // open the admin panel (isAdmin). The client calls this after login to gate the admin button/route.
    // Returns 401 if the token's user no longer exists (account deleted) so the client re-authenticates.
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me()
    {
        try
        {
            if (!int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
                return Unauthorized();

            var me = await _userAdminService.GetMeAsync(userId);
            return me is null ? Unauthorized() : Ok(me);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Me));
        }
    }
}
