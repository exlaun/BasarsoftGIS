using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for PUT /api/admin/{users|roles}/{id}/geo-area: the authorized Polygon or
// MultiPolygon as WKT (EPSG:4326 lon-lat). Other geometry types are rejected as a 400.
public class GeoAreaRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;
}
