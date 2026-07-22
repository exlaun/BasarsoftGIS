using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Position-only update for an existing stop. Route, order, name, and color remain immutable here.
public class StopMoveRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;
}
