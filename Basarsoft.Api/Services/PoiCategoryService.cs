using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Basarsoft.Api.Services;

public class PoiCategoryService : IPoiCategoryService
{
    private readonly AppDbContext _db;

    public PoiCategoryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PoiCategoryResponse>> ListAsync()
    {
        var categories = await _db.PoiCategories.OrderBy(c => c.Id).ToListAsync();

        // Direct live-POI count per category (the Pois query filter hides deleted/inactive rows).
        var counts = await _db.Pois
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        return categories.Select(c => new PoiCategoryResponse
        {
            Id = c.Id,
            Name = c.Name,
            ParentId = c.ParentId,
            Color = c.Color,
            IconKey = c.IconKey,
            PoiCount = counts.TryGetValue(c.Id, out var count) ? count : 0,
        }).ToList();
    }

    public async Task<PoiCategoryWriteResult> CreateAsync(PoiCategorySaveRequest request, int userId)
    {
        var name = request.Name.Trim();

        if (!PoiIconCatalog.TryNormalize(request.IconKey, out var iconKey))
            return PoiCategoryWriteResult.InvalidIcon;

        if (request.ParentId is int parentId && !await _db.PoiCategories.AnyAsync(c => c.Id == parentId))
            return PoiCategoryWriteResult.InvalidParent;

        // Sibling names must be unique — the same name under different parents is fine
        // ("Diğer" can exist under both "Yeme İçme" and "Alışveriş").
        if (await _db.PoiCategories.AnyAsync(c => c.ParentId == request.ParentId && c.Name == name))
            return PoiCategoryWriteResult.Conflict;

        var category = new PoiCategory
        {
            Name = name,
            ParentId = request.ParentId,
            Color = request.Color,
            IconKey = iconKey,
            UserId = userId,
            ModifiedUserId = userId,
        };
        _db.PoiCategories.Add(category);
        await _db.SaveChangesAsync();

        return PoiCategoryWriteResult.Ok(new PoiCategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            ParentId = category.ParentId,
            Color = category.Color,
            IconKey = category.IconKey,
            PoiCount = 0,
        });
    }

    public async Task<PoiCategoryWriteResult> UpdateAsync(int id, PoiCategorySaveRequest request, int userId)
    {
        var category = await _db.PoiCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
            return PoiCategoryWriteResult.NotFound;

        if (!PoiIconCatalog.TryNormalize(request.IconKey, out var iconKey))
            return PoiCategoryWriteResult.InvalidIcon;

        if (request.ParentId is int parentId)
        {
            // A category can't be its own parent, and the new parent must exist and must not sit
            // anywhere below this category — otherwise the "tree" gains a cycle and every
            // path/tree walk over it would loop forever.
            if (parentId == id)
                return PoiCategoryWriteResult.InvalidParent;

            var all = await _db.PoiCategories.ToDictionaryAsync(c => c.Id);
            if (!all.ContainsKey(parentId))
                return PoiCategoryWriteResult.InvalidParent;

            var ancestorId = (int?)parentId;
            for (var depth = 0; ancestorId is not null && depth < 20; depth++)
            {
                if (ancestorId.Value == id)
                    return PoiCategoryWriteResult.InvalidParent;
                ancestorId = all.TryGetValue(ancestorId.Value, out var ancestor) ? ancestor.ParentId : null;
            }
        }

        var name = request.Name.Trim();
        if (await _db.PoiCategories.AnyAsync(c => c.Id != id && c.ParentId == request.ParentId && c.Name == name))
            return PoiCategoryWriteResult.Conflict;

        category.Name = name;
        category.ParentId = request.ParentId;
        category.Color = request.Color;
        category.IconKey = iconKey;
        category.ModifiedUserId = userId;

        // The ancestor walk above is check-then-write: two concurrent re-parents (A under B, B under
        // A) can each pass their check and still commit a cycle. Save inside a transaction and
        // re-walk from the DB state; if a cycle appeared, roll back instead of persisting it.
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();

            if (request.ParentId is not null && await IsInCycleAsync(id))
            {
                await transaction.RollbackAsync();
                return PoiCategoryWriteResult.InvalidParent;
            }

            await transaction.CommitAsync();
        }

        var poiCount = await _db.Pois.CountAsync(p => p.CategoryId == id);
        return PoiCategoryWriteResult.Ok(new PoiCategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            ParentId = category.ParentId,
            Color = category.Color,
            IconKey = category.IconKey,
            PoiCount = poiCount,
        });
    }

    // Walks the parent chain of `id` from FRESH database state (AsNoTracking — a tracked query
    // would hand back this request's already-loaded instances instead of what a concurrent writer
    // committed) and reports whether the chain leads back to `id`. Depth-capped like every other
    // tree walk over this table.
    private async Task<bool> IsInCycleAsync(int id)
    {
        var parents = await _db.PoiCategories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.ParentId);

        var current = parents.GetValueOrDefault(id);
        for (var depth = 0; current is not null && depth < 20; depth++)
        {
            if (current.Value == id)
                return true;
            current = parents.GetValueOrDefault(current.Value);
        }
        return false;
    }

    public async Task<PoiCategoryWriteStatus> DeleteAsync(int id, int userId)
    {
        var category = await _db.PoiCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
            return PoiCategoryWriteStatus.NotFound;

        // Refuse to orphan anything: live children or live POIs keep the category alive. (The DB's
        // Restrict FK can't help here because a soft delete never touches the referencing rows.)
        if (await _db.PoiCategories.AnyAsync(c => c.ParentId == id) ||
            await _db.Pois.AnyAsync(p => p.CategoryId == id))
            return PoiCategoryWriteStatus.InUse;

        category.IsDeleted = true;
        category.ModifiedUserId = userId;
        await _db.SaveChangesAsync();
        return PoiCategoryWriteStatus.Ok;
    }
}
