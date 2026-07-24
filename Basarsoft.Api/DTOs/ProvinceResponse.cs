namespace Basarsoft.Api.DTOs;

// GET /api/provinces list row: just enough for the location-analysis dropdown. The boundary is fetched
// per province on selection (ProvinceDetailResponse) so the list stays a few hundred bytes.
public class ProvinceResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

// GET /api/provinces/{id}: the boundary as WKT (EPSG:4326 lon-lat, MultiPolygon) — the same geometry
// transport every other endpoint uses, so the client reuses its WKT reader to draw the outline.
public class ProvinceDetailResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Wkt { get; set; } = string.Empty;
}

// GET /api/provinces/map: one boundary/capital pair for the nationwide reference layer. Both
// geometries use EPSG:4326 WKT and share the same province id/color so the client can style and
// highlight the relationship without attempting a spatial join in the browser.
public class ProvinceMapResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public string BoundaryWkt { get; set; } = string.Empty;

    public string CapitalName { get; set; } = string.Empty;

    public string CapitalWkt { get; set; } = string.Empty;
}
