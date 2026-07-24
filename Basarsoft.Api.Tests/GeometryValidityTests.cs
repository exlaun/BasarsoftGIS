using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Xunit;

namespace Basarsoft.Api.Tests;

// Topological-validity guard: WKT that parses but describes an invalid shape (a self-intersecting
// "bow-tie" polygon) must be rejected as InvalidGeometry BEFORE it reaches PostGIS, where it would
// poison later ST_Within/ST_Intersects counts or throw mid-query on unrelated requests.
public class GeometryValidityTests
{
    private const string BowTieWkt = "POLYGON((0 0, 2 2, 2 0, 0 2, 0 0))";

    private static GeometryService NewService() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options),
        new UnrestrictedGeoAuthorizationService());

    [Fact]
    public async Task Create_SelfIntersectingPolygon_IsInvalidGeometry()
    {
        var result = await NewService().CreateAsync(
            "polygon", new GeometryCreateRequest { Wkt = BowTieWkt, Name = "bad" }, userId: 1);

        Assert.Equal(UpdateStatus.InvalidGeometry, result.Status);
    }

    [Fact]
    public async Task Analyze_SelfIntersectingPolygon_IsRejected()
    {
        Assert.Null(await NewService().AnalyzeAsync(BowTieWkt, userId: 1));
    }

    [Fact]
    public async Task Create_ValidPolygonWkt_PassesTheValidityGuard()
    {
        // Same coordinates, drawn without the crossing — proves the guard rejects the topology,
        // not the syntax. (Reaches the DB and saves; the in-memory provider is fine with that.)
        var result = await NewService().CreateAsync(
            "polygon",
            new GeometryCreateRequest { Wkt = "POLYGON((0 0, 2 0, 2 2, 0 2, 0 0))", Name = "ok" },
            userId: 1);

        Assert.Equal(UpdateStatus.Success, result.Status);
    }

    [Fact]
    public async Task Create_PolygonCountsInventoriesThatCrossItsBoundary()
    {
        var service = NewService();

        var line = await service.CreateAsync(
            "line",
            new GeometryCreateRequest
            {
                Wkt = "LINESTRING(-1 1, 3 1)",
                Name = "crossing line",
            },
            userId: 1);

        Assert.Equal(UpdateStatus.Success, line.Status);

        var polygon = await service.CreateAsync(
            "polygon",
            new GeometryCreateRequest
            {
                Wkt = "POLYGON((0 0, 2 0, 2 2, 0 2, 0 0))",
                Name = "analysis area",
            },
            userId: 1);

        Assert.Equal(UpdateStatus.Success, polygon.Status);
        Assert.Equal(1, polygon.Response?.IntersectionCount);
    }

    // Geo-authorization stub: no area assigned, so drawing is unrestricted. Only
    // GetEffectiveAreaAsync is ever consulted by GeometryService.
    private sealed class UnrestrictedGeoAuthorizationService : IGeoAuthorizationService
    {
        public Task<Geometry?> GetEffectiveAreaAsync(int userId) => Task.FromResult<Geometry?>(null);

        public Task<GeoAreaResponse?> GetForUserAsync(int userId) => throw new NotSupportedException();
        public Task<GeoAreaResponse?> GetForRoleAsync(int roleId) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForUserAsync(int userId, string wkt) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForRoleAsync(int roleId, string wkt) => throw new NotSupportedException();
        public Task<bool> ClearForUserAsync(int userId) => throw new NotSupportedException();
        public Task<bool> ClearForRoleAsync(int roleId) => throw new NotSupportedException();
    }
}
