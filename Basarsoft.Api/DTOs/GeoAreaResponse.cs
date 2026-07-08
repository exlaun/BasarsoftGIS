namespace Basarsoft.Api.DTOs;

// Returned by GET /api/admin/{users|roles}/{id}/geo-area. Wkt is null when the target exists but has
// no authorized area assigned (a missing target is a 404 instead).
public class GeoAreaResponse
{
    public string? Wkt { get; set; }

    public DateTime? ModifiedDate { get; set; }
}
