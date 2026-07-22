using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace Basarsoft.Api.Tests;

// The authorized area binds every write, not just the create paths: you may not delete or edit a
// feature you would not have been allowed to place. Each case seeds through an UNRESTRICTED service
// and then acts through a restricted one over the same DbContext, which is exactly what happens in
// production when an admin shrinks (or first assigns) a user's area after they have drawn.
public class GeoAuthorizationEnforcementTests
{
    // The authorized area used throughout: the unit square at the origin. (10 10) is far outside it.
    private const string UnitSquare = "POLYGON((0 0, 0 1, 1 1, 1 0, 0 0))";

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static GeometryService Geometry(AppDbContext db, string? areaWkt = null) =>
        new(db, Area(areaWkt));

    private static PoiService Pois(AppDbContext db, string? areaWkt = null) =>
        new(db, Area(areaWkt));

    private static IGeoAuthorizationService Area(string? wkt) =>
        wkt is null ? new NoAreaStub() : new FixedAreaStub(wkt);

    private static async Task<int> SeedShapeAsync(GeometryService service, string wkt)
    {
        var result = await service.CreateAsync(
            "point", new GeometryCreateRequest { Wkt = wkt, Name = "seed" }, userId: 1);
        Assert.Equal(UpdateStatus.Success, result.Status);
        return result.Response!.Id;
    }

    private static async Task<int> SeedPoiAsync(AppDbContext db, string wkt)
    {
        // Inserted directly rather than through CreateAsync: that path needs a live category row,
        // and none of these assertions depend on category resolution.
        var geom = new WKTReader().Read(wkt);
        geom.SRID = 4326;
        var poi = new Poi { UserId = 1, Name = "seed", Geom = geom, CategoryId = 1 };
        db.Pois.Add(poi);
        await db.SaveChangesAsync();
        return poi.Id;
    }

    [Fact]
    public async Task DeleteShape_OutsideAuthorizedArea_IsRejectedAndTheShapeSurvives()
    {
        var db = NewDb();
        var stranded = await SeedShapeAsync(Geometry(db), "POINT(10 10)");

        var restricted = Geometry(db, UnitSquare);
        Assert.Equal(
            DeleteStatus.OutsideAuthorizedArea,
            await restricted.DeleteAsync("point", stranded, userId: 1));

        // Refused before the soft delete, so the row is still listable.
        Assert.Equal([stranded], (await restricted.ListAsync("point", userId: 1)).Select(s => s.Id));
    }

    [Fact]
    public async Task DeleteShape_InsideAuthorizedArea_StillSucceeds()
    {
        var db = NewDb();
        var local = await SeedShapeAsync(Geometry(db), "POINT(0.5 0.5)");

        var restricted = Geometry(db, UnitSquare);
        Assert.Equal(DeleteStatus.Success, await restricted.DeleteAsync("point", local, userId: 1));
        Assert.Empty(await restricted.ListAsync("point", userId: 1));
    }

    [Fact]
    public async Task DeleteShape_WithNoAssignedArea_IsUnrestricted()
    {
        // The property the whole change leans on: a null effective area is a no-op, so admins and
        // any other user without an assigned polygon are completely unaffected.
        var db = NewDb();
        var unrestricted = Geometry(db);
        var far = await SeedShapeAsync(unrestricted, "POINT(10 10)");

        Assert.Equal(DeleteStatus.Success, await unrestricted.DeleteAsync("point", far, userId: 1));
    }

    [Fact]
    public async Task UpdateShape_AttributesOnlyOnAnOutOfAreaShape_IsRejected()
    {
        // The gap this closes: with no WKT in the request there is no new geometry to test, so the
        // old code never consulted the area at all and happily renamed a shape far outside it.
        var db = NewDb();
        var stranded = await SeedShapeAsync(Geometry(db), "POINT(10 10)");

        var result = await Geometry(db, UnitSquare).UpdateAsync(
            "point", stranded, new GeometryUpdateRequest { Name = "renamed" }, userId: 1);

        Assert.Equal(UpdateStatus.OutsideAuthorizedArea, result.Status);
    }

    [Fact]
    public async Task UpdateShape_DraggingAnOutOfAreaShapeInside_IsStillRejected()
    {
        // You must be entitled to the shape where it currently sits, so an out-of-area shape cannot
        // be "rescued" by moving it in — otherwise the boundary would be trivially escapable.
        var db = NewDb();
        var stranded = await SeedShapeAsync(Geometry(db), "POINT(10 10)");

        var result = await Geometry(db, UnitSquare).UpdateAsync(
            "point",
            stranded,
            new GeometryUpdateRequest { Name = "seed", Wkt = "POINT(0.5 0.5)" },
            userId: 1);

        Assert.Equal(UpdateStatus.OutsideAuthorizedArea, result.Status);
    }

    [Fact]
    public async Task UpdateShape_MovingAnInAreaShapeOut_IsRejected()
    {
        var db = NewDb();
        var local = await SeedShapeAsync(Geometry(db), "POINT(0.5 0.5)");

        var result = await Geometry(db, UnitSquare).UpdateAsync(
            "point",
            local,
            new GeometryUpdateRequest { Name = "seed", Wkt = "POINT(10 10)" },
            userId: 1);

        Assert.Equal(UpdateStatus.OutsideAuthorizedArea, result.Status);
    }

    [Fact]
    public async Task UpdateShape_InsideToInside_StillSucceeds()
    {
        var db = NewDb();
        var local = await SeedShapeAsync(Geometry(db), "POINT(0.2 0.2)");

        var result = await Geometry(db, UnitSquare).UpdateAsync(
            "point",
            local,
            new GeometryUpdateRequest { Name = "moved", Wkt = "POINT(0.8 0.8)" },
            userId: 1);

        Assert.Equal(UpdateStatus.Success, result.Status);
        Assert.Equal("moved", result.Response!.Name);
    }

    [Fact]
    public async Task DeletePoi_OutsideAuthorizedArea_IsRejectedAndThePoiSurvives()
    {
        var db = NewDb();
        var stranded = await SeedPoiAsync(db, "POINT(10 10)");

        Assert.Equal(
            DeleteStatus.OutsideAuthorizedArea,
            await Pois(db, UnitSquare).DeleteAsync(stranded, userId: 1, isAdmin: true));

        Assert.False(await db.Pois.AnyAsync(p => p.Id == stranded && p.IsDeleted));
    }

    [Fact]
    public async Task DeletePoi_InsideAuthorizedArea_StillSucceeds()
    {
        var db = NewDb();
        var local = await SeedPoiAsync(db, "POINT(0.5 0.5)");

        Assert.Equal(
            DeleteStatus.Success,
            await Pois(db, UnitSquare).DeleteAsync(local, userId: 1, isAdmin: true));
    }

    [Fact]
    public async Task DeletePoi_WithNoAssignedArea_IsUnrestricted()
    {
        var db = NewDb();
        var far = await SeedPoiAsync(db, "POINT(10 10)");

        Assert.Equal(DeleteStatus.Success, await Pois(db).DeleteAsync(far, userId: 1, isAdmin: true));
    }

    // No area assigned -> unrestricted. Only GetEffectiveAreaAsync is consulted; IsOutsideAreaAsync
    // is a default interface method derived from it, so these stubs need nothing else.
    private sealed class NoAreaStub : GeoAuthorizationStub
    {
        public override Task<Geometry?> GetEffectiveAreaAsync(int userId) => Task.FromResult<Geometry?>(null);
    }

    private sealed class FixedAreaStub : GeoAuthorizationStub
    {
        private readonly Geometry _area;

        public FixedAreaStub(string wkt)
        {
            _area = new WKTReader().Read(wkt);
            _area.SRID = 4326;
        }

        public override Task<Geometry?> GetEffectiveAreaAsync(int userId) => Task.FromResult<Geometry?>(_area);
    }

    // Only the enforcement-side method matters here; the admin CRUD surface is never reached.
    private abstract class GeoAuthorizationStub : IGeoAuthorizationService
    {
        public abstract Task<Geometry?> GetEffectiveAreaAsync(int userId);

        public Task<GeoAreaResponse?> GetForUserAsync(int userId) => throw new NotSupportedException();
        public Task<GeoAreaResponse?> GetForRoleAsync(int roleId) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForUserAsync(int userId, string wkt) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForRoleAsync(int roleId, string wkt) => throw new NotSupportedException();
        public Task<bool> ClearForUserAsync(int userId) => throw new NotSupportedException();
        public Task<bool> ClearForRoleAsync(int roleId) => throw new NotSupportedException();
    }
}
