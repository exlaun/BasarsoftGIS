namespace Basarsoft.Api.DTOs;

// One route as the Route Management panel consumes it: identity, color, how many stops it has, and
// audit info. The stop geometries are fetched separately (GET /api/routes/{id}/stops).
public class RouteResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // The route's own color ("#rrggbb"), or null to use the client's default stop color.
    public string? Color { get; set; }

    // How many live stops belong to this route — shown next to the route in the panel list.
    public int StopCount { get; set; }

    // users.id of the creator.
    public int UserId { get; set; }

    // The creator's username, shown in the panel / info views.
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ModifiedDate { get; set; }
}
