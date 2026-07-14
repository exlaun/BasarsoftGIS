using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for POST /api/poi. The point is sent as WKT (EPSG:4326 lon-lat) like every other geometry;
// there is deliberately NO user id — the creator comes from the JWT. CategoryId/OpenTime/CloseTime
// are nullable so a missing field fails [Required] with a 400 instead of silently defaulting to
// category 0 / 00:00.
public class PoiCreateRequest
{
    [Required]
    public string Wkt { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    // Must be one of the admin-prepared categories (tbl_poi_category.id).
    [Required]
    public int? CategoryId { get; set; }

    // Working hours (mesai saatleri). The client sends "HH:mm" from <input type="time">;
    // System.Text.Json parses that into TimeOnly out of the box.
    [Required]
    public TimeOnly? OpenTime { get; set; }

    [Required]
    public TimeOnly? CloseTime { get; set; }
}
