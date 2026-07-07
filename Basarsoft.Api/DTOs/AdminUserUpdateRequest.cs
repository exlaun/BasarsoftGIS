using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for updating a user from the admin panel. NewPassword is optional: supply it to reset the
// password (re-hashed), leave it null/empty to keep the current one. MinLength is only enforced when a
// value is present (the attribute treats null as valid).
public class AdminUserUpdateRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [MinLength(6)]
    public string? NewPassword { get; set; }
}
