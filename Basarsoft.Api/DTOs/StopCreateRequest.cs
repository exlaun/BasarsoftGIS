using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for POST /api/stops. The point is sent as WKT (EPSG:4326 lon-lat) like every other geometry;
// there is deliberately NO user id (creator comes from the JWT) and NO sequence order (the service
// assigns the next position in the route). RouteId is nullable so a missing field fails [Required]
// with a 400 instead of silently defaulting to route 0.
public class StopCreateRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    // The route this stop belongs to (tbl_route.id). Immutable after create.
    [Required]
    public int? RouteId { get; set; }
}
