using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for POST /api/geometry/analysis. The client draws a temporary polygon (never saved)
// and sends it as WKT (EPSG:4326); the server counts how many of the caller's shapes intersect it.
public class AnalysisRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;
}
