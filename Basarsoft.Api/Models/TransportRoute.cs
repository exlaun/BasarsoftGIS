namespace Basarsoft.Api.Models;

// One transportation route -> tbl_route. A route is just a named, colored grouping of stops; it has
// no geometry of its own (the line is not modeled). Every stop points at exactly one route. Modeled on
// PoiCategory (a name + color reference row) minus the tree fields. Soft-deleted like the other tables,
// though the module exposes no route deletion.
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

    // FK -> users.id. The operator who created the route. Stamped from the JWT, never from the client.
    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete: hidden via a global query filter instead of being physically removed.
    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    // FK -> users.id. Who last changed the route (rename / recolor).
    public int? ModifiedUserId { get; set; }

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
