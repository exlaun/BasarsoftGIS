namespace Basarsoft.Api.DTOs;

// One row of the category tree, kept deliberately flat: the client rebuilds the hierarchy from
// ParentId (indented admin tree + depth-prefixed operator dropdown) instead of receiving nested JSON.
public class PoiCategoryResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Null = top-level category.
    public int? ParentId { get; set; }

    // How many live POIs use this category directly (children not included). Shown in the admin
    // tree and the reason a delete may be refused.
    public int PoiCount { get; set; }
}
