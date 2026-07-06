using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Query-string parameters for GET /api/geometry/query — the server-side filtered/sorted/paged list
// behind the query panel. Everything here becomes SQL (WHERE / ORDER BY / LIMIT-OFFSET); the client
// never filters or sorts rows itself. Range/length violations 400 automatically via [ApiController];
// SortBy/SortDir/Types are whitelisted in the service (bad values -> 400 from the controller).
public class GeometryQueryRequest
{
    // Case-insensitive "contains" filter on the shape name. Blank = no name filter.
    [MaxLength(80)]
    public string? Name { get; set; }

    // CSV of geometry types to include, e.g. "point,polygon". Blank = all three.
    public string? Types { get; set; }

    // Whitelist: name | createdAt.
    public string SortBy { get; set; } = "createdAt";

    // Whitelist: asc | desc.
    public string SortDir { get; set; } = "desc";

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 10;
}
