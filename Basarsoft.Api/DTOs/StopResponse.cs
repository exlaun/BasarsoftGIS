namespace Basarsoft.Api.DTOs;

// One stop as the map layer and the info popup consume it. Carries its route (id + name + color) so
// the client can tint the marker and show which route the stop belongs to without a second lookup.
public class StopResponse
{
    public int Id { get; set; }

    // The point as WKT (EPSG:4326), parsed straight back into an OpenLayers feature.
    public string Wkt { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int RouteId { get; set; }

    public string RouteName { get; set; } = string.Empty;

    // The owning route's color ("#rrggbb") or null — the client tints the marker with it.
    public string? RouteColor { get; set; }

    // Position within the route, 1..N with no gaps.
    public int SequenceOrder { get; set; }

    // users.id of the creator.
    public int UserId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ModifiedDate { get; set; }
}
