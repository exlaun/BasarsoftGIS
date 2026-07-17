namespace Basarsoft.Api.DTOs;

// One row of the category tree, kept deliberately flat: the client rebuilds the hierarchy from
// ParentId (indented admin tree + depth-prefixed operator dropdown) instead of receiving nested JSON.
public class PoiCategoryResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Null = top-level category.
    public int? ParentId { get; set; }

    // This category's OWN color ("#rrggbb") — null means "inherits from an ancestor", so the admin
    // tree can tell an explicit color from an inherited one.
    public string? Color { get; set; }

    // This category's OWN icon key. Null means "inherit from an ancestor"; clients can resolve the
    // flat tree locally while retaining the distinction between an explicit and inherited icon.
    public string? IconKey { get; set; }

    // How many live POIs use this category directly (children not included). Shown in the admin
    // tree and the reason a delete may be refused.
    public int PoiCount { get; set; }
}
