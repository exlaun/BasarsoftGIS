using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace Basarsoft.Api.Tests;

// Unit tests for the transportation service: per-route sequence-order assignment, reorder renumbering
// and its exact-match guard, deletion (survivor renumbering, geometry clearing, route->stop cascade),
// duplicate route names, stop point/WKT validation, and the geo-auth area check. The in-memory provider
// no-ops the (route_id, sequence_order) index and check constraints, so these cover the app-level rules;
// the DB-level pieces stay covered by the curl matrix against Postgres.
public class TransportationServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TransportationService NewService(
        AppDbContext db,
        IGeoAuthorizationService? geoAuth = null,
        IRoutingService? routing = null) =>
        new(db, geoAuth ?? new UnrestrictedGeoAuthorizationService(), routing ?? new RecordingRoutingService());

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
        Assert.NotNull(result.Route?.GeometryWkt);
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
    public async Task AdminSnapshot_GroupsEachRoutesOrderedStops()
    {
        var db = NewDb();
        var service = NewService(db);
        var firstRoute = await SeedRouteAsync(service, "First");
        var secondRoute = await SeedRouteAsync(service, "Second");
        await service.CreateStopAsync(StopAt("POINT(1 1)", firstRoute, "One"), 1);
        await service.CreateStopAsync(StopAt("POINT(2 2)", firstRoute, "Two"), 1);
        await service.CreateStopAsync(StopAt("POINT(3 3)", secondRoute, "Other"), 1);

        var snapshot = await service.GetAdminSnapshotAsync();

        Assert.Equal(2, snapshot.Routes.Count);
        var first = Assert.Single(snapshot.Routes, group => group.Route.Id == firstRoute);
        Assert.Equal(["One", "Two"], first.Stops.Select(stop => stop.Name));
        var second = Assert.Single(snapshot.Routes, group => group.Route.Id == secondRoute);
        Assert.Equal("Other", Assert.Single(second.Stops).Name);
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

    // The area binds removal as well as placement. Each case seeds through an UNRESTRICTED service and
    // then acts through a restricted one over the same DbContext — what happens in production when an
    // admin assigns or shrinks an operator's area after the data already exists.
    private const string UnitSquare = "POLYGON((0 0, 0 1, 1 1, 1 0, 0 0))";

    [Fact]
    public async Task DeleteStop_OutsideAuthorizedArea_IsRejectedAndNothingIsCommitted()
    {
        var db = NewDb();
        var seeder = NewService(db);
        var routeId = await SeedRouteAsync(seeder);
        await seeder.CreateStopAsync(StopAt("POINT(0.2 0.2)", routeId, "Inside"), userId: 1);
        var stranded = await seeder.CreateStopAsync(StopAt("POINT(10 10)", routeId, "Outside"), userId: 1);

        var restricted = NewService(db, new FixedAreaGeoAuthorizationService(UnitSquare));
        var result = await restricted.DeleteStopAsync(stranded.Response!.Id, userId: 1);

        Assert.Equal(TransportWriteStatus.OutsideAuthorizedArea, result.Status);
        // Refused before the soft delete and the renumbering, so there is no partial success to report.
        Assert.False(result.OrderPersisted);
        var stops = await seeder.ListRouteStopsAsync(routeId);
        Assert.Equal(["Inside", "Outside"], stops!.Select(stop => stop.Name));
        Assert.Equal([1, 2], stops!.Select(stop => stop.SequenceOrder));
    }

    [Fact]
    public async Task DeleteRoute_WhoseLineEscapesTheArea_IsRejectedAndCascadesNothing()
    {
        var db = NewDb();
        var seeder = NewService(db);
        var routeId = await SeedRouteAsync(seeder, "Nationwide");
        await seeder.CreateStopAsync(StopAt("POINT(0.2 0.2)", routeId), userId: 1);
        await seeder.CreateStopAsync(StopAt("POINT(10 10)", routeId), userId: 1);

        // Two stops, so the route carries a built line that runs well outside the unit square.
        var restricted = NewService(db, new FixedAreaGeoAuthorizationService(UnitSquare));
        Assert.Equal(
            DeleteStatus.OutsideAuthorizedArea,
            await restricted.DeleteRouteAsync(routeId, userId: 1));

        // The cascade is the reason this matters: neither the route nor either stop may be touched.
        Assert.Equal([routeId], (await seeder.ListRoutesAsync()).Select(route => route.Id));
        Assert.Equal(2, (await seeder.ListRouteStopsAsync(routeId))!.Count);
    }

    [Fact]
    public async Task DeleteRoute_NeverBuilt_FallsBackToItsStops()
    {
        var db = NewDb();
        var seeder = NewService(db);
        var restricted = NewService(db, new FixedAreaGeoAuthorizationService(UnitSquare));

        // A single stop leaves the route's geometry null, so the rule falls back to the stop positions.
        var stranded = await SeedRouteAsync(seeder, "Unbuilt far");
        await seeder.CreateStopAsync(StopAt("POINT(10 10)", stranded), userId: 1);
        Assert.Equal(
            DeleteStatus.OutsideAuthorizedArea,
            await restricted.DeleteRouteAsync(stranded, userId: 1));

        // ...and a route entirely inside the area is still deletable through that same fallback.
        var local = await SeedRouteAsync(seeder, "Unbuilt local");
        await seeder.CreateStopAsync(StopAt("POINT(0.4 0.4)", local), userId: 1);
        Assert.Equal(DeleteStatus.Success, await restricted.DeleteRouteAsync(local, userId: 1));
    }

    [Fact]
    public async Task RouteLevelWrites_OutsideTheArea_AreRejected()
    {
        var db = NewDb();
        var seeder = NewService(db);
        var routeId = await SeedRouteAsync(seeder, "Nationwide");
        var first = await seeder.CreateStopAsync(StopAt("POINT(0.2 0.2)", routeId), userId: 1);
        var second = await seeder.CreateStopAsync(StopAt("POINT(10 10)", routeId), userId: 1);

        var restricted = NewService(db, new FixedAreaGeoAuthorizationService(UnitSquare));

        var renamed = await restricted.UpdateRouteAsync(
            routeId, new RouteSaveRequest { Name = "Renamed" }, userId: 1);
        Assert.Equal(TransportWriteStatus.OutsideAuthorizedArea, renamed.Status);

        var reordered = await restricted.ReorderStopsAsync(
            routeId, [second.Response!.Id, first.Response!.Id], userId: 1);
        Assert.Equal(TransportWriteStatus.OutsideAuthorizedArea, reordered.Status);

        var rebuilt = await restricted.RebuildRouteAsync(routeId, userId: 1);
        Assert.Equal(TransportWriteStatus.OutsideAuthorizedArea, rebuilt.Status);

        // Nothing moved: the original order survives every refusal.
        Assert.Equal([1, 2], (await seeder.ListRouteStopsAsync(routeId))!.Select(stop => stop.SequenceOrder));
    }

    [Fact]
    public async Task BuildRoute_FewerThanTwoStops_DoesNotCallRouting()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService();
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1);

        var result = await service.RebuildRouteAsync(routeId, userId: 1);

        Assert.Equal(TransportWriteStatus.InsufficientStops, result.Status);
        Assert.Equal(0, routing.CallCount);
        Assert.Null(result.Route!.GeometryWkt);
    }

    [Fact]
    public async Task SecondStop_AutomaticallyPersistsRoutedGeometryAndMetrics()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService();
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1);

        var result = await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1);

        Assert.Equal(TransportWriteStatus.Success, result.Status);
        Assert.Equal(1, routing.CallCount);
        var route = await db.Routes.SingleAsync(r => r.Id == routeId);
        Assert.NotNull(route.Geometry);
        Assert.Equal(2500, route.DistanceMeters);
        Assert.Equal(300, route.DurationSeconds);
        Assert.False(route.IsGeometryStale);
    }

    [Fact]
    public async Task Reorder_RebuildsUsingThePersistedNewOrder()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService();
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        var id1 = (await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1)).Response!.Id;
        var id2 = (await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1)).Response!.Id;
        var id3 = (await service.CreateStopAsync(StopAt("POINT(3 3)", routeId), 1)).Response!.Id;

        var result = await service.ReorderStopsAsync(routeId, [id3, id1, id2], 1);

        Assert.Equal(TransportWriteStatus.Success, result.Status);
        Assert.Equal(new[] { 3d, 1d, 2d }, routing.LastCoordinates.Select(c => c.X));
        Assert.Equal(new[] { id3, id1, id2 }, result.Stops!.Select(s => s.Id));
    }

    [Fact]
    public async Task FailedRebuild_PreservesPreviousGeometryAndMarksRouteStale()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService();
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        var id1 = (await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1)).Response!.Id;
        var id2 = (await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1)).Response!.Id;
        var before = (await db.Routes.SingleAsync(r => r.Id == routeId)).Geometry!.AsText();
        routing.Status = RoutingStatus.Unavailable;

        var result = await service.ReorderStopsAsync(routeId, [id2, id1], 1);

        Assert.Equal(TransportWriteStatus.RoutingUnavailable, result.Status);
        Assert.True(result.OrderPersisted);
        var route = await db.Routes.SingleAsync(r => r.Id == routeId);
        Assert.Equal(before, route.Geometry!.AsText());
        Assert.True(route.IsGeometryStale);
        Assert.Equal("routing_unavailable", route.RoutingErrorCode);
    }

    [Fact]
    public async Task FailedAutomaticRebuild_KeepsNewStopAndPreviousGeometry()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService();
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1);
        await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1);
        var previousGeometry = (await db.Routes.SingleAsync(r => r.Id == routeId)).Geometry!.AsText();
        routing.Status = RoutingStatus.NoRoute;

        var result = await service.CreateStopAsync(StopAt("POINT(3 3)", routeId), 1);

        Assert.Equal(TransportWriteStatus.NoRoute, result.Status);
        Assert.True(result.StopPersisted);
        Assert.Equal(3, await db.Stops.CountAsync(s => s.RouteId == routeId));
        var route = await db.Routes.SingleAsync(r => r.Id == routeId);
        Assert.Equal(previousGeometry, route.Geometry!.AsText());
        Assert.True(route.IsGeometryStale);
        Assert.Equal("no_route", route.RoutingErrorCode);
    }

    [Fact]
    public async Task FailedFirstBuild_RemainsNotBuiltAndCarriesLastError()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService { Status = RoutingStatus.Unavailable };
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1);

        var result = await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1);

        Assert.Equal(TransportWriteStatus.RoutingUnavailable, result.Status);
        var route = await db.Routes.SingleAsync(r => r.Id == routeId);
        Assert.Null(route.Geometry);
        Assert.False(route.IsGeometryStale);
        Assert.Equal("routing_unavailable", route.RoutingErrorCode);
    }

    [Fact]
    public async Task DeleteStop_RenumbersSurvivorsAndRebuildsWithoutIt()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService();
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        var id1 = (await service.CreateStopAsync(StopAt("POINT(1 1)", routeId, "One"), 1)).Response!.Id;
        var id2 = (await service.CreateStopAsync(StopAt("POINT(2 2)", routeId, "Two"), 1)).Response!.Id;
        var id3 = (await service.CreateStopAsync(StopAt("POINT(3 3)", routeId, "Three"), 1)).Response!.Id;

        var result = await service.DeleteStopAsync(id2, userId: 1);

        Assert.Equal(TransportWriteStatus.Success, result.Status);
        Assert.True(result.OrderPersisted);
        // SequenceOrder is 1..N with no gaps, so the survivors close the hole rather than staying 1, 3.
        Assert.Equal(new[] { id1, id3 }, result.Stops!.Select(s => s.Id));
        Assert.Equal(new[] { 1, 2 }, result.Stops!.Select(s => s.SequenceOrder));
        // The rebuild that follows routes through the survivors only — the deleted waypoint is gone.
        Assert.Equal(new[] { 1d, 3d }, routing.LastCoordinates.Select(c => c.X));
        Assert.Equal(2, result.Route!.StopCount);
        Assert.Equal(new[] { 1, 2 }, (await service.ListRouteStopsAsync(routeId))!.Select(s => s.SequenceOrder));
    }

    [Fact]
    public async Task DeleteStop_DroppingBelowTwoStops_ClearsTheRoutesGeometry()
    {
        var db = NewDb();
        var service = NewService(db);
        var routeId = await SeedRouteAsync(service);
        var id1 = (await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1)).Response!.Id;
        await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1);
        Assert.NotNull((await db.Routes.SingleAsync(r => r.Id == routeId)).Geometry);

        var result = await service.DeleteStopAsync(id1, userId: 1);

        // A one-stop route has nothing to connect, so keeping the old line would draw it to a stop
        // that no longer exists — the route goes back to "Not built" rather than "Stale".
        Assert.Equal(TransportWriteStatus.Success, result.Status);
        var route = await db.Routes.SingleAsync(r => r.Id == routeId);
        Assert.Null(route.Geometry);
        Assert.Null(route.DistanceMeters);
        Assert.Null(route.DurationSeconds);
        Assert.False(route.IsGeometryStale);
        Assert.Null(route.RoutingErrorCode);
    }

    [Fact]
    public async Task DeleteStop_FailedRebuild_StillRemovesTheStop()
    {
        var db = NewDb();
        var routing = new RecordingRoutingService();
        var service = NewService(db, routing: routing);
        var routeId = await SeedRouteAsync(service);
        await service.CreateStopAsync(StopAt("POINT(1 1)", routeId), 1);
        await service.CreateStopAsync(StopAt("POINT(2 2)", routeId), 1);
        var id3 = (await service.CreateStopAsync(StopAt("POINT(3 3)", routeId), 1)).Response!.Id;
        routing.Status = RoutingStatus.Unavailable;

        var result = await service.DeleteStopAsync(id3, userId: 1);

        // The delete is committed before the network call, so routing failing must never bring it back.
        Assert.Equal(TransportWriteStatus.RoutingUnavailable, result.Status);
        Assert.True(result.OrderPersisted);
        Assert.Equal(2, await db.Stops.CountAsync(s => s.RouteId == routeId));
        Assert.True((await db.Routes.SingleAsync(r => r.Id == routeId)).IsGeometryStale);
    }

    [Fact]
    public async Task DeleteStop_UnknownId_IsStopNotFound()
    {
        var result = await NewService(NewDb()).DeleteStopAsync(999, userId: 1);
        Assert.Equal(TransportWriteStatus.StopNotFound, result.Status);
    }

    [Fact]
    public async Task DeleteRoute_TakesItsStopsWithIt()
    {
        var db = NewDb();
        var service = NewService(db);
        var doomed = await SeedRouteAsync(service, "Doomed");
        var survivor = await SeedRouteAsync(service, "Survivor");
        await service.CreateStopAsync(StopAt("POINT(1 1)", doomed), 1);
        await service.CreateStopAsync(StopAt("POINT(2 2)", doomed), 1);
        await service.CreateStopAsync(StopAt("POINT(3 3)", survivor), 1);

        Assert.Equal(DeleteStatus.Success, await service.DeleteRouteAsync(doomed, userId: 1));

        // A live stop must always have a live route, so the cascade is what keeps ListAllStopsAsync's
        // assumption true instead of leaving stops no query can reach.
        Assert.Equal([survivor], (await service.ListRoutesAsync()).Select(r => r.Id));
        Assert.All(await service.ListAllStopsAsync(), stop => Assert.Equal(survivor, stop.RouteId));
        Assert.Null(await service.ListRouteStopsAsync(doomed));
    }

    [Fact]
    public async Task DeleteRoute_FreesItsNameAndUnknownIdIsNotFound()
    {
        var db = NewDb();
        var service = NewService(db);
        var routeId = await SeedRouteAsync(service, "Line 12");

        Assert.Equal(DeleteStatus.Success, await service.DeleteRouteAsync(routeId, userId: 1));

        // Uniqueness is checked among live routes only, so the name comes free with the deletion.
        var recreated = await service.CreateRouteAsync(new RouteSaveRequest { Name = "Line 12" }, userId: 1);
        Assert.Equal(TransportWriteStatus.Success, recreated.Status);
        Assert.Equal(DeleteStatus.NotFound, await service.DeleteRouteAsync(999, userId: 1));
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

    private sealed class RecordingRoutingService : IRoutingService
    {
        public RoutingStatus Status { get; set; } = RoutingStatus.Success;
        public int CallCount { get; private set; }
        public IReadOnlyList<Coordinate> LastCoordinates { get; private set; } = [];

        public Task<RoutingResult> BuildRouteAsync(
            IReadOnlyList<Coordinate> orderedCoordinates,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCoordinates = orderedCoordinates.Select(c => c.Copy()).ToList();
            if (Status != RoutingStatus.Success)
                return Task.FromResult(Status switch
                {
                    RoutingStatus.NoRoute => RoutingResult.NoRoute,
                    RoutingStatus.InvalidCoordinates => RoutingResult.InvalidCoordinates,
                    _ => RoutingResult.Unavailable,
                });

            var line = new LineString(orderedCoordinates.Select(c => c.Copy()).ToArray()) { SRID = 4326 };
            return Task.FromResult(RoutingResult.Ok(line, 2500, 300));
        }
    }
}
