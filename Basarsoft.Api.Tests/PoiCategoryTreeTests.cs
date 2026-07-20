using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Basarsoft.Api.Tests;

// Cycle-prevention rules for the self-referencing category tree: a category may never become its
// own ancestor, or every breadcrumb/inheritance walk over the table would loop (they are
// depth-capped, so in practice they'd silently truncate).
public class PoiCategoryTreeTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // UpdateAsync saves inside a transaction (real Postgres); the in-memory provider
            // ignores transactions, which is fine for these single-writer tests.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static PoiCategory NewCategory(int id, string name, int? parentId = null) => new()
    {
        Id = id,
        Name = name,
        ParentId = parentId,
        UserId = 1,
    };

    private static PoiCategorySaveRequest Save(string name, int? parentId) => new()
    {
        Name = name,
        ParentId = parentId,
    };

    [Fact]
    public async Task Update_CategoryAsItsOwnParent_IsRejected()
    {
        await using var db = NewDb();
        db.PoiCategories.Add(NewCategory(1, "Food"));
        await db.SaveChangesAsync();

        var service = new PoiCategoryService(db);
        var result = await service.UpdateAsync(1, Save("Food", parentId: 1), userId: 1);

        Assert.Equal(PoiCategoryWriteStatus.InvalidParent, result.Status);
    }

    [Fact]
    public async Task Update_DirectChildAsParent_IsRejected()
    {
        await using var db = NewDb();
        db.PoiCategories.AddRange(
            NewCategory(1, "Food"),
            NewCategory(2, "Cafe", parentId: 1));
        await db.SaveChangesAsync();

        var service = new PoiCategoryService(db);
        var result = await service.UpdateAsync(1, Save("Food", parentId: 2), userId: 1);

        Assert.Equal(PoiCategoryWriteStatus.InvalidParent, result.Status);
        Assert.Null((await db.PoiCategories.SingleAsync(c => c.Id == 1)).ParentId);
    }

    [Fact]
    public async Task Update_DeepDescendantAsParent_IsRejected()
    {
        await using var db = NewDb();
        db.PoiCategories.AddRange(
            NewCategory(1, "Food"),
            NewCategory(2, "Cafe", parentId: 1),
            NewCategory(3, "Specialty", parentId: 2));
        await db.SaveChangesAsync();

        var service = new PoiCategoryService(db);
        var result = await service.UpdateAsync(1, Save("Food", parentId: 3), userId: 1);

        Assert.Equal(PoiCategoryWriteStatus.InvalidParent, result.Status);
    }

    [Fact]
    public async Task Update_ReparentToUnrelatedCategory_Succeeds()
    {
        await using var db = NewDb();
        db.PoiCategories.AddRange(
            NewCategory(1, "Food"),
            NewCategory(2, "Cafe", parentId: 1),
            NewCategory(3, "Shops"));
        await db.SaveChangesAsync();

        var service = new PoiCategoryService(db);
        var result = await service.UpdateAsync(2, Save("Cafe", parentId: 3), userId: 1);

        Assert.Equal(PoiCategoryWriteStatus.Ok, result.Status);
        Assert.Equal(3, (await db.PoiCategories.SingleAsync(c => c.Id == 2)).ParentId);
    }
}
