using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for POST /api/geometry/{type}. The client sends the shape as WKT text
// (in EPSG:4326 lon-lat). There is deliberately NO user id here — the server takes the owner
// from the JWT so a client can't claim to be someone else.
public class GeometryCreateRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;

    // Optional label for the shape.
    public string? Name { get; set; }
}
