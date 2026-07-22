using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One transportation route -> tbl_route. Stops define its waypoint order; Geometry is the latest
// successful OSRM road line in EPSG:4326. A failed rebuild never overwrites that last good line.
//
// Named TransportRoute (not Route) to avoid colliding with Microsoft.AspNetCore.Routing.Route, which
// the web SDK's implicit usings bring into every file. The DB table, REST path, and UI all stay "route".
public class TransportRoute : IAuditable
{
    public int Id { get; set; }

    // Route label shown in the panel list, the stop form's dropdown, and stop popups, e.g. "Line 12".
    public string Name { get; set; } = string.Empty;

    // Route color ("#rrggbb"), used to tint this route's stop markers on the map. Null = the map's
    // default stop color.
    public string? Color { get; set; }

    public LineString? Geometry { get; set; }

    public double? DistanceMeters { get; set; }

    public double? DurationSeconds { get; set; }

    // True when the stop order/location changed but the most recent routing attempt did not produce a
    // replacement. Geometry may still contain the last valid line so the map can render useful data.
    public bool IsGeometryStale { get; set; }

    // One of no_route / invalid_coordinates / routing_unavailable, or null after a successful build.
    public string? RoutingErrorCode { get; set; }

    // FK -> users.id. The operator who created the route. Stamped from the JWT, never from the client.
    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete: hidden via a global query filter instead of being physically removed.
    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    // FK -> users.id. Who last changed route presentation, stop order, or routing state.
    public int? ModifiedUserId { get; set; }

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
