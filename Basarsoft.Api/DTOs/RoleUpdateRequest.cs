using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for updating a role's name/description.
public class RoleUpdateRequest
{
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }
}
