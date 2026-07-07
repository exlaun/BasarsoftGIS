using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for creating a user from the admin panel. Password is hashed with the same BCrypt call as
// self-registration; RoleIds optionally grants roles at creation time.
public class AdminUserCreateRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    public IReadOnlyList<int> RoleIds { get; set; } = Array.Empty<int>();
}
