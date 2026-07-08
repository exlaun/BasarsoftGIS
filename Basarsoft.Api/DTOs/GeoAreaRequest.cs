using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Incoming body for PUT /api/admin/{users|roles}/{id}/geo-area: the authorized-area polygon as WKT
// (EPSG:4326 lon-lat). Must parse to a single valid POLYGON — anything else is rejected as a 400.
public class GeoAreaRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;
}
