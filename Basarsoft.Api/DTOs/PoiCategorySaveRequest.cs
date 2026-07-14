using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Shared body for creating and updating a POI category. ParentId null = top-level; otherwise it
// must reference an existing category and (on update) must not create a cycle — both checked in
// PoiCategoryService, which is the authoritative guard.
public class PoiCategorySaveRequest
{
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public int? ParentId { get; set; }
}
