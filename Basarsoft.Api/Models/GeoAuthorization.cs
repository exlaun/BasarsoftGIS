using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// A geographic authorization area: a polygon assigned to EITHER a user OR a role
// (exactly one — enforced by a check constraint). A user with an effective area may only draw
// geometries inside it. Resolution rule: the user's own area overrides role areas; otherwise the
// union of the user's roles' areas applies; no rows at all means unrestricted drawing.
public class GeoAuthorization : IAuditable
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int? RoleId { get; set; }

    // geometry(Polygon,4326) — one polygon per row; effective multi-area shapes only ever exist
    // in memory as the union of rows, never in this column.
    public Geometry Geom { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
