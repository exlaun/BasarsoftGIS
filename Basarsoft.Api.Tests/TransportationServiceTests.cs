using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace Basarsoft.Api.Tests;

// Unit tests for the transportation service: per-route sequence-order assignment, reorder renumbering
// and its exact-match guard, duplicate route names, stop point/WKT validation, and the geo-auth area
// check. The in-memory provider no-ops the (route_id, sequence_order) index and check constraints, so
// these cover the app-level rules; the DB-level pieces stay covered by the curl matrix against Postgres.
public class TransportationServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TransportationService NewService(AppDbContext db, IGeoAuthorizationService? geoAuth = null) =>
        new(db, geoAuth ?? new UnrestrictedGeoAuthorizationService());

    private static StopCreateRequest StopAt(string wkt, int routeId, string name = "Stop") =>
        new() { Wkt = wkt, Name = name, RouteId = routeId };

    private static async Task<int> SeedRouteAsync(TransportationService service, string name = "Route A")
    {
        var result = await service.CreateRouteAsync(new RouteSaveRequest { Name = name }, userId: 1);
        Assert.Equal(TransportWriteStatus.Success, result.Status);
        return result.Response!.Id;
    }

    [Fact]
    public async Task CreateStop_AppendsSequenceOrder1ToN()
    {
        var db = NewDb();
        var service = NewService(db);
        var routeId = await SeedRouteAsync(service);

        var s1 = await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), userId: 1);
        var s2 = await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), userId: 1);
        var s3 = await service.CreateStopAsync(StopAt("POINT(3 3)", routeId), userId: 1);

        Assert.Equal(TransportWriteStatus.Success, s1.Status);
        Assert.Equal(1, s1.Response!.SequenceOrder);
        Assert.Equal(2, s2.Response!.SequenceOrder);
        Assert.Equal(3, s3.Response!.SequenceOrder);
    }

    [Theory]
    [InlineData("NOT WKT")]
    [InlineData("LINESTRING(0 0, 1 1)")] // parses, but is not a single point
    public async Task CreateStop_NonPointWkt_IsInvalidGeometry(string wkt)
    {
        var db = NewDb();
        var service = NewService(db);
        var routeId = await SeedRouteAsync(service);

        var result = await service.CreateStopAsync(StopAt(wkt, routeId), userId: 1);
        Assert.Equal(TransportWriteStatus.InvalidGeometry, result.Status);
    }

    [Fact]
    public async Task CreateStop_UnknownRoute_IsRouteNotFound()
    {
        var db = NewDb();
        var result = await NewService(db).CreateStopAsync(StopAt("POINT(1 1)", routeId: 999), userId: 1);
        Assert.Equal(TransportWriteStatus.RouteNotFound, result.Status);
    }

    [Fact]
    public async Task CreateRoute_DuplicateName_IsRejected()
    {
        var db = NewDb();
        var service = NewService(db);
        await SeedRouteAsync(service, "Line 12");

        var dup = await service.CreateRouteAsync(new RouteSaveRequest { Name = "Line 12" }, userId: 1);
        Assert.Equal(TransportWriteStatus.DuplicateName, dup.Status);
    }

    [Fact]
    public async Task ReorderStops_RenumbersToSubmittedOrder()
    {
        var db = NewDb();
        var service = NewService(db);
        var routeId = await SeedRouteAsync(service);
        var id1 = (await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1)).Response!.Id;
        var id2 = (await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1)).Response!.Id;
        var id3 = (await service.CreateStopAsync(StopAt("POINT(3 3)", routeId), 1)).Response!.Id;

        var result = await service.ReorderStopsAsync(routeId, new[] { id3, id1, id2 }, userId: 1);
        Assert.Equal(TransportWriteStatus.Success, result.Status);

        var ordered = await service.ListRouteStopsAsync(routeId);
        Assert.NotNull(ordered);
        Assert.Equal(new[] { id3, id1, id2 }, ordered!.Select(stop => stop.Id));
        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(stop => stop.SequenceOrder));
    }

    [Fact]
    public async Task ReorderStops_IdsDoNotMatchRoute_IsInvalidOrder()
    {
        var db = NewDb();
        var service = NewService(db);
        var routeId = await SeedRouteAsync(service);
        var id1 = (await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1)).Response!.Id;
        var id2 = (await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1)).Response!.Id;

        // Incomplete set (a stop is missing).
        var incomplete = await service.ReorderStopsAsync(routeId, new[] { id1 }, userId: 1);
        Assert.Equal(TransportWriteStatus.InvalidOrder, incomplete.Status);

        // A stranger id that isn't one of this route's stops.
        var stranger = await service.ReorderStopsAsync(routeId, new[] { id1, id2, 999 }, userId: 1);
        Assert.Equal(TransportWriteStatus.InvalidOrder, stranger.Status);
    }

    [Fact]
    public async Task CreateStop_OutsideAuthorizedArea_IsRejected()
    {
        var db = NewDb();
        // Authorized area = the unit square at the origin; (10 10) is well outside it.
        var service = NewService(db, new FixedAreaGeoAuthorizationService("POLYGON((0 0, 0 1, 1 1, 1 0, 0 0))"));
        var routeId = await SeedRouteAsync(service);

        var inside = await service.CreateStopAsync(StopAt("POINT(0.5 0.5)", routeId), userId: 1);
        Assert.Equal(TransportWriteStatus.Success, inside.Status);

        var outside = await service.CreateStopAsync(StopAt("POINT(10 10)", routeId), userId: 1);
        Assert.Equal(TransportWriteStatus.OutsideAuthorizedArea, outside.Status);
    }

    // Geo-auth stub: no area assigned, so placement is unrestricted. Only GetEffectiveAreaAsync is
    // consulted by the service (same shape as GeometryValidityTests' stub).
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

    // Geo-auth stub returning a fixed authorized polygon, to exercise the Covers check.
    private sealed class FixedAreaGeoAuthorizationService : IGeoAuthorizationService
    {
        private readonly Geometry _area;

        public FixedAreaGeoAuthorizationService(string wkt)
        {
            _area = new WKTReader().Read(wkt);
            _area.SRID = 4326;
        }

        public Task<Geometry?> GetEffectiveAreaAsync(int userId) => Task.FromResult<Geometry?>(_area);

        public Task<GeoAreaResponse?> GetForUserAsync(int userId) => throw new NotSupportedException();
        public Task<GeoAreaResponse?> GetForRoleAsync(int roleId) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForUserAsync(int userId, string wkt) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForRoleAsync(int roleId, string wkt) => throw new NotSupportedException();
        public Task<bool> ClearForUserAsync(int userId) => throw new NotSupportedException();
        public Task<bool> ClearForRoleAsync(int roleId) => throw new NotSupportedException();
    }
}
