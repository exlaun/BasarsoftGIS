using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for creating a role.
public class RoleCreateRequest
{
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }
}
