using Basarsoft.Api.Data;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Xunit;

namespace Basarsoft.Api.Tests;

public class GeoAuthorizationServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SetForUser_Polygon_NormalizesAndRoundTripsAsMultiPolygon()
    {
        await using var db = NewDb();
        db.Users.Add(new User { Id = 1, Username = "area-user", PasswordHash = "x" });
        await db.SaveChangesAsync();
        var service = new GeoAuthorizationService(db);

        var status = await service.SetForUserAsync(
            1, "POLYGON((30 39,31 39,31 40,30 40,30 39))");

        Assert.Equal(GeoAreaWriteStatus.Success, status);
        var stored = await db.GeoAuthorizations.SingleAsync();
        var multiPolygon = Assert.IsType<MultiPolygon>(stored.Geom);
        Assert.Equal(4326, multiPolygon.SRID);
        Assert.Single(multiPolygon.Geometries);

        var response = await service.GetForUserAsync(1);
        Assert.NotNull(response);
        Assert.StartsWith("MULTIPOLYGON", response.Wkt);
    }

    [Fact]
    public async Task SetForRole_DisconnectedMultiPolygon_PreservesEveryComponent()
    {
        await using var db = NewDb();
        db.Roles.Add(new Role { Id = 1, Name = "Region" });
        await db.SaveChangesAsync();
        var service = new GeoAuthorizationService(db);
        const string wkt =
            "MULTIPOLYGON(((26 39,27 39,27 40,26 40,26 39)),((29 40,30 40,30 41,29 41,29 40)))";

        var status = await service.SetForRoleAsync(1, wkt);

        Assert.Equal(GeoAreaWriteStatus.Success, status);
        var stored = Assert.IsType<MultiPolygon>((await db.GeoAuthorizations.SingleAsync()).Geom);
        Assert.Equal(2, stored.NumGeometries);

        var response = await service.GetForRoleAsync(1);
        Assert.NotNull(response);
        Assert.StartsWith("MULTIPOLYGON", response.Wkt);
    }

    [Fact]
    public async Task SetForRole_OverlappingComponents_DissolvesToValidMultiPolygon()
    {
        await using var db = NewDb();
        db.Roles.Add(new Role { Id = 1, Name = "Region" });
        await db.SaveChangesAsync();
        var service = new GeoAuthorizationService(db);
        const string wkt =
            "MULTIPOLYGON(((26 39,28 39,28 41,26 41,26 39)),((27 40,29 40,29 42,27 42,27 40)))";

        var status = await service.SetForRoleAsync(1, wkt);

        Assert.Equal(GeoAreaWriteStatus.Success, status);
        var stored = Assert.IsType<MultiPolygon>((await db.GeoAuthorizations.SingleAsync()).Geom);
        Assert.True(stored.IsValid);
        Assert.Single(stored.Geometries);
        Assert.Equal(7, stored.Area, precision: 8);
        Assert.True(stored.Covers(new Point(26.5, 39.5)));
        Assert.True(stored.Covers(new Point(28.5, 41.5)));
    }

    [Theory]
    [InlineData("LINESTRING(30 39,31 40)")]
    [InlineData("POLYGON((0 0,1 1,1 0,0 1,0 0))")]
    [InlineData("MULTIPOLYGON EMPTY")]
    public async Task SetForUser_NonPolygonalInvalidOrEmptyGeometry_IsRejected(string wkt)
    {
        await using var db = NewDb();
        db.Users.Add(new User { Id = 1, Username = "area-user", PasswordHash = "x" });
        await db.SaveChangesAsync();
        var service = new GeoAuthorizationService(db);

        Assert.Equal(GeoAreaWriteStatus.InvalidGeometry, await service.SetForUserAsync(1, wkt));
        Assert.Empty(db.GeoAuthorizations);
    }
}
