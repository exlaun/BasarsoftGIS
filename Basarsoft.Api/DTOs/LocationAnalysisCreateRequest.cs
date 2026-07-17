using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for POST /api/location-analysis. The target region arrives EITHER as a province id (dropdown)
// OR as drawn-polygon WKT — exactly one, checked in the service (annotations can't express XOR).
// The mentor's hard rule "the analysis must not start unless the weights sum to exactly 100" is
// enforced in the service too; annotations only cover the per-field shapes.
public class LocationAnalysisCreateRequest
{
    // tbl_province.id when the user picked a province from the dropdown.
    public int? ProvinceId { get; set; }

    // Drawn region as WKT (EPSG:4326 lon-lat, Polygon or MultiPolygon) when the user drew on the map.
    public string? RegionWkt { get; set; }

    // 2..5 weighted criteria (mentor's bounds). MinLength/MaxLength on a list = element count.
    [Required]
    [MinLength(2)]
    [MaxLength(5)]
    public List<LocationAnalysisCriterionRequest> Criteria { get; set; } = new();
}

public class LocationAnalysisCriterionRequest
{
    // tbl_poi_category.id — a main category matches its whole subtree, a sub category just itself
    // (and its own descendants). Nullable so a missing field fails [Required] instead of becoming 0.
    [Required]
    public int? CategoryId { get; set; }

    // Importance score out of 100. 1..100: a zero-weight criterion would defeat the min-2 rule
    // in spirit. All weights of one request must total exactly 100 (service check).
    [Required]
    [Range(1, 100)]
    public int? Weight { get; set; }
}
