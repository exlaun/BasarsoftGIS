using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for POST /api/auth/register (used to create the first user).
public class RegisterRequest
{
    [Required]
    [MinLength(3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;
}
