using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public interface IPoiCategoryService
{
    // All live categories as a flat list (the client rebuilds the tree from ParentId), each with its
    // direct live-POI count. Feeds both the admin tree and the operator's dropdown.
    Task<IReadOnlyList<PoiCategoryResponse>> ListAsync();

    // Creates a category. Conflict on a duplicate name under the same parent; InvalidParent when the
    // parent id doesn't exist.
    Task<PoiCategoryWriteResult> CreateAsync(PoiCategorySaveRequest request, int userId);

    // Renames and/or re-parents a category. Same checks as create, plus the cycle guard: the new
    // parent may not be the category itself or any of its descendants.
    Task<PoiCategoryWriteResult> UpdateAsync(int id, PoiCategorySaveRequest request, int userId);

    // Soft-deletes a category. InUse when live children or live POIs still point at it.
    Task<PoiCategoryWriteStatus> DeleteAsync(int id, int userId);
}
