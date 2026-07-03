using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for PUT /api/geometry/{type}/{id}. Updates a shape the caller owns.
// As with create, there is deliberately NO user id here — ownership is taken from the JWT and
// re-checked server-side, so a client can't edit someone else's shape by guessing an id.
public class GeometryUpdateRequest
{
    // Required label (same rule as create): [Required] also rejects whitespace-only values, and
    // [MaxLength] bounds the DB `text` column so the client's 80-char cap can't be bypassed.
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    // Display color (hex). Optional, but when supplied it must be a 6-digit hex value.
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a 6-digit hex value like #2563eb.")]
    public string? Color { get; set; }

    // New geometry as WKT (EPSG:4326). Optional: null/empty means "attributes only, leave the shape
    // where it is". When present it must parse to the SAME geometry type as the shape being edited.
    public string? Wkt { get; set; }
}
