using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// A geographic authorization area: a polygon or disconnected multipolygon assigned to EITHER a user
// OR a role
// (exactly one — enforced by a check constraint). A user with an effective area may only draw
// geometries inside it. Resolution rule: the user's own area overrides role areas; otherwise the
// union of the user's roles' areas applies; no rows at all means unrestricted drawing.
public class GeoAuthorization : IAuditable
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int? RoleId { get; set; }

    // geometry(MultiPolygon,4326). API callers may submit Polygon or MultiPolygon WKT; the service
    // normalizes both to MultiPolygon before persistence so islands and regional unions are retained.
    public Geometry Geom { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
