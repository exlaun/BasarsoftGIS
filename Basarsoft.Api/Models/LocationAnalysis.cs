using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One saved run of the "Konum Analizi" (location analysis) tool -> tbl_location_analysis. The row
// exists so GeoServer's vw_konum view can be parameterized with nothing but this row's integer id
// (viewparams aid:<id>): region polygon and criteria live here instead of being squeezed through the
// URL, which kills both the SQL-injection surface and any URL-length limit. Carries the same audit
// column set as the drawing tables (mentor convention).
public class LocationAnalysis : IAuditable
{
    public int Id { get; set; }

    // FK -> users.id. Who ran the analysis; the WMS proxy only serves an analysis to its owner.
    public int UserId { get; set; }

    // The resolved target region (WGS84 lon-lat). Always stored as MultiPolygon: a drawn Polygon is
    // wrapped, a picked province is copied as-is — downstream code never branches on the source.
    public Geometry Geom { get; set; } = default!;

    // FK -> tbl_province.id when the region came from the province dropdown; null for a drawn region.
    public int? ProvinceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public int? ModifiedUserId { get; set; }

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
