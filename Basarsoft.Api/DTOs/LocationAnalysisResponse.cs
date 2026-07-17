namespace Basarsoft.Api.DTOs;

// Reply to POST /api/location-analysis. `Id` is the handle the client passes back to
// GET /api/location-analysis/{id}/wms to render the weighted heat map; the echoed region/criteria
// let the panel build its legend without re-deriving anything.
public class LocationAnalysisResponse
{
    public int Id { get; set; }

    // Set when the region came from the province dropdown; null for a drawn region.
    public int? ProvinceId { get; set; }

    public string? ProvinceName { get; set; }

    // The resolved region as WKT (EPSG:4326, MultiPolygon) — drawn or province, always the same shape.
    public string RegionWkt { get; set; } = string.Empty;

    // How many live POIs inside the region match at least one criterion (each POI counted once).
    // Lets the client warn "0 matches — the heat map will be empty" instead of showing a blank layer.
    public int MatchedPoiCount { get; set; }

    public List<LocationAnalysisCriterionResponse> Criteria { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}

public class LocationAnalysisCriterionResponse
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public int Weight { get; set; }
}
