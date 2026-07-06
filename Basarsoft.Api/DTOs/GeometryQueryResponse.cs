namespace Basarsoft.Api.DTOs;

// One row of the query panel. Deliberately carries NO WKT: the map already holds every feature
// client-side, so the panel finds a row's shape by (Type, Id) instead of re-shipping geometry.
public class GeometryListItem
{
    public int Id { get; set; }

    // "point" | "line" | "polygon" — which table the row came from.
    public string Type { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ModifiedDate { get; set; }
}

// One page of query-panel rows plus the paging metadata the client needs to render page controls.
public class GeometryQueryResponse
{
    public IReadOnlyList<GeometryListItem> Items { get; set; } = Array.Empty<GeometryListItem>();

    // Total matching rows across ALL pages (for "page X of Y" and the result count).
    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
