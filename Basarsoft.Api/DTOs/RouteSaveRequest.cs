using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Shared body for creating and updating a transportation route. There is deliberately no user id —
// the creator/editor comes from the JWT. Name uniqueness among live routes is enforced in
// TransportationService (so the stop form's dropdown never shows two identical labels).
public class RouteSaveRequest
{
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    // Optional "#rrggbb" color used to tint this route's stop markers. Null = the map's default.
    [RegularExpression("^#[0-9a-fA-F]{6}$")]
    public string? Color { get; set; }
}
