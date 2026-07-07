using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for adding a permission to the shared catalogue. Name is the machine key (e.g. "point_ekleme").
public class PermissionCreateRequest
{
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }
}
