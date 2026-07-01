using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for POST /api/geometry/{type}. The client sends the shape as WKT text
// (in EPSG:4326 lon-lat). There is deliberately NO user id here — the server takes the owner
// from the JWT so a client can't claim to be someone else.
public class GeometryCreateRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;

    // Required label for the shape — the mentor's spec requires at least a Name. [Required] also rejects
    // whitespace-only values (RequiredAttribute trims strings), and [MaxLength] bounds the DB `text` column
    // so the client's 80-char cap can't be bypassed via a direct API call.
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    // Display color for the shape (hex string, e.g. "#2563eb"), picked by the user in the save popup.
    // Optional at the API level, but when supplied it must be a 6-digit hex color.
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a 6-digit hex value like #2563eb.")]
    public string? Color { get; set; }
}
