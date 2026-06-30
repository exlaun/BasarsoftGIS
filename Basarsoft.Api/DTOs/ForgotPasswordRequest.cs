using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for POST /api/auth/forgot-password (step 1: confirm the username exists).
public class ForgotPasswordRequest
{
    [Required]
    [MinLength(3)]
    public string Username { get; set; } = string.Empty;
}
