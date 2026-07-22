using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

public class StopUpdateRequest
{
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    // Optional "#rrggbb" override for this one stop's marker. Null = inherit the route's color, which
    // is what every stop did before per-stop color existed. Same pattern as RouteSaveRequest.Color.
    [RegularExpression("^#[0-9a-fA-F]{6}$")]
    public string? Color { get; set; }
}
