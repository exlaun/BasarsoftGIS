using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One stop on a transportation route -> tbl_stop. Implements IGeoFeature so it reuses the shared
// geometry-table mapping (sequence id, geometry(Point,4326) column, soft-delete filter, user FKs).
// Like a POI it is a point the operator places on the map, but it belongs to exactly one route
// (RouteId) and carries a per-route SequenceOrder the service manages (1..N, reorderable) — the same
// "ordered child of a parent" shape as LocationAnalysisCriterion's Weight.
public class Stop : IGeoFeature
{
    public int Id { get; set; }

    // FK -> users.id. The operator who added the stop. Stamped from the JWT, never from the client.
    public int UserId { get; set; }

    public string? Name { get; set; }

    // Unused for stops (they render in their route's color), but required by IGeoFeature.
    public string? Color { get; set; }

    public Geometry Geom { get; set; } = default!;

    // FK -> tbl_route.id. Every stop belongs to exactly one route; the value never changes after create
    // (the module exposes no "move stop between routes"). Restrict-on-delete backs that at the DB level.
    public int RouteId { get; set; }

    // Position of this stop within its route, 1..N with no gaps. App-managed (NOT a DB sequence): the
    // service assigns max+1 on create and renumbers the whole route on reorder.
    public int SequenceOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public int? ModifiedUserId { get; set; }

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
