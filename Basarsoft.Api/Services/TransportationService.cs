using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Basarsoft.Api.Services;

public class TransportationService : ITransportationService
{
    private readonly AppDbContext _db;
    private readonly IGeoAuthorizationService _geoAuth;
    private readonly IRoutingService _routing;
    private readonly IRouteSimulationStateReader _simulationStates;
    private readonly WKTReader _wktReader = new();

    // Same storage CRS as every other geometry table: EPSG:4326 (WGS84 lon-lat).
    private const int Srid = 4326;

    public TransportationService(
        AppDbContext db,
        IGeoAuthorizationService geoAuth,
        IRoutingService routing,
        IRouteSimulationStateReader simulationStates)
    {
        _db = db;
        _geoAuth = geoAuth;
        _routing = routing;
        _simulationStates = simulationStates;
    }

    public async Task<IReadOnlyList<RouteResponse>> ListRoutesAsync()
    {
        var routes = await _db.Routes.OrderBy(r => r.Id).ToListAsync();

        // Live stop count per route (the Stops query filter hides deleted/inactive rows).
        var counts = await _db.Stops
            .GroupBy(s => s.RouteId)
            .Select(g => new { RouteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RouteId, x => x.Count);

        var usernames = await UsernameMapAsync(routes.Select(r => r.UserId));

        return routes.Select(r => ToRouteResponse(
            r,
            counts.TryGetValue(r.Id, out var count) ? count : 0,
            usernames.GetValueOrDefault(r.UserId, string.Empty))).ToList();
    }

    public async Task<RouteWriteResult> CreateRouteAsync(RouteSaveRequest request, int userId)
    {
        var name = request.Name.Trim();

        // Unique among live routes so the stop form's dropdown never shows two identical labels.
        if (await _db.Routes.AnyAsync(r => r.Name == name))
            return RouteWriteResult.DuplicateName;

        var route = new TransportRoute
        {
            Name = name,
            Color = request.Color,
            UserId = userId,
            ModifiedUserId = userId,
        };
        _db.Routes.Add(route);
        await _db.SaveChangesAsync();

        return RouteWriteResult.Ok(ToRouteResponse(route, 0, await UsernameAsync(userId)));
    }

    // A route-level operation is bound by the route's full extent, not by any single stop: its built
    // road line, or — when it has never been built — the stops that are the only positions it
    // actually occupies. The area is fetched once and compared in memory against every geometry.
    // Null area (nobody assigned the caller one) means unrestricted, so this is a no-op for admins.
    private async Task<bool> IsRouteOutsideAreaAsync(
        TransportRoute route,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var area = await _geoAuth.GetEffectiveAreaAsync(userId);
        if (area is null)
            return false;

        if (route.Geometry is not null)
            return !area.Covers(route.Geometry);

        var stops = await _db.Stops
            .Where(s => s.RouteId == route.Id)
            .ToListAsync(cancellationToken);
        return stops.Any(stop => !area.Covers(stop.Geom));
    }

    public async Task<RouteWriteResult> UpdateRouteAsync(int id, RouteSaveRequest request, int userId)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (route is null)
            return RouteWriteResult.NotFound;

        if (await IsRouteOutsideAreaAsync(route, userId))
            return RouteWriteResult.OutsideAuthorizedArea;

        var name = request.Name.Trim();
        if (await _db.Routes.AnyAsync(r => r.Id != id && r.Name == name))
            return RouteWriteResult.DuplicateName;

        route.Name = name;
        route.Color = request.Color;
        route.ModifiedUserId = userId;
        await _db.SaveChangesAsync();

        var stopCount = await _db.Stops.CountAsync(s => s.RouteId == id);
        return RouteWriteResult.Ok(ToRouteResponse(route, stopCount, await UsernameAsync(route.UserId)));
    }

    public async Task<IReadOnlyList<StopResponse>?> ListRouteStopsAsync(int routeId)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == routeId);
        if (route is null)
            return null;

        var stops = await _db.Stops
            .Where(s => s.RouteId == routeId)
            .OrderBy(s => s.SequenceOrder)
            .ToListAsync();

        var usernames = await UsernameMapAsync(stops.Select(s => s.UserId));
        return stops
            .Select(s => ToStopResponse(s, route, usernames.GetValueOrDefault(s.UserId, string.Empty)))
            .ToList();
    }

    public async Task<IReadOnlyList<StopResponse>> ListAllStopsAsync()
    {
        var stops = await _db.Stops
            .OrderBy(s => s.RouteId).ThenBy(s => s.SequenceOrder)
            .ToListAsync();
        var routes = await _db.Routes.ToDictionaryAsync(r => r.Id);
        var usernames = await UsernameMapAsync(stops.Select(s => s.UserId));

        return stops
            // A live stop always has a live route (route FK is Restrict), but guard anyway.
            .Where(s => routes.ContainsKey(s.RouteId))
            .Select(s => ToStopResponse(s, routes[s.RouteId], usernames.GetValueOrDefault(s.UserId, string.Empty)))
            .ToList();
    }

    public async Task<AdminTransportationResponse> GetAdminSnapshotAsync()
    {
        var routes = await ListRoutesAsync();
        var stops = await ListAllStopsAsync();
        var stopsByRoute = stops
            .GroupBy(stop => stop.RouteId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<StopResponse>)group.ToList());

        return new AdminTransportationResponse
        {
            Routes = routes.Select(route => new AdminTransportationRouteResponse
            {
                Route = route,
                Stops = stopsByRoute.GetValueOrDefault(route.Id, Array.Empty<StopResponse>()),
            }).ToList(),
        };
    }

    public async Task<StopWriteResult> CreateStopAsync(
        StopCreateRequest request,
        int userId,
        CancellationToken cancellationToken = default)
    {
        // Parse the WKT the client drew. Bad text -> InvalidGeometry -> the controller returns 400.
        Geometry geom;
        try
        {
            geom = _wktReader.Read(request.Wkt);
        }
        catch
        {
            return StopWriteResult.InvalidGeometry;
        }

        if (geom is null || geom.IsEmpty || geom.OgcGeometryType != OgcGeometryType.Point)
            return StopWriteResult.InvalidGeometry;

        geom.SRID = Srid;

        var routeId = request.RouteId!.Value;
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == routeId);
        if (route is null)
            return StopWriteResult.RouteNotFound;
        if (_simulationStates.IsRunning(routeId))
            return StopWriteResult.SimulationRunning;

        // Same geographic authorization as every other draw tool: with an assigned area, stops may
        // only be placed inside it.
        if (await _geoAuth.IsOutsideAreaAsync(userId, geom))
            return StopWriteResult.OutsideAuthorizedArea;

        // Append to the end of the route: next order is the current max + 1 (1 for the first stop).
        var maxOrder = await _db.Stops
            .Where(s => s.RouteId == routeId)
            .Select(s => (int?)s.SequenceOrder)
            .MaxAsync() ?? 0;

        var stop = new Stop
        {
            UserId = userId,
            Name = request.Name,
            Geom = geom,
            RouteId = routeId,
            SequenceOrder = maxOrder + 1,
            CreatedAt = DateTime.UtcNow,
            // A never-edited stop reports its creator as the last modifier, like the other tables.
            ModifiedUserId = userId,
        };
        _db.Stops.Add(stop);
        await _db.SaveChangesAsync(cancellationToken);

        RouteBuildResult rebuild;
        if (stop.SequenceOrder >= 2)
        {
            rebuild = await RebuildRouteCoreAsync(route, userId, cancellationToken);
        }
        else
        {
            rebuild = RouteBuildResult.From(
                TransportWriteStatus.InsufficientStops,
                ToRouteResponse(route, 1, await UsernameAsync(route.UserId)));
        }

        var response = ToStopResponse(stop, route, await UsernameAsync(userId));
        var status = rebuild.Status == TransportWriteStatus.InsufficientStops
            ? TransportWriteStatus.Success
            : rebuild.Status;
        return new StopWriteResult(status, response, rebuild.Route, StopPersisted: true);
    }

    public async Task<StopWriteResult> MoveStopAsync(
        int id,
        StopMoveRequest request,
        int userId,
        CancellationToken cancellationToken = default)
    {
        Geometry destination;
        try
        {
            destination = _wktReader.Read(request.Wkt);
        }
        catch
        {
            return StopWriteResult.InvalidGeometry;
        }

        if (destination is null || destination.IsEmpty ||
            destination.OgcGeometryType != OgcGeometryType.Point)
            return StopWriteResult.InvalidGeometry;

        destination.SRID = Srid;

        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stop is null)
            return StopWriteResult.StopNotFound;

        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == stop.RouteId, cancellationToken);
        if (route is null)
            return StopWriteResult.RouteNotFound;
        if (_simulationStates.IsRunning(route.Id))
            return StopWriteResult.SimulationRunning;

        // Movement is authorized at both ends: assigning/shrinking an area must not let an operator
        // pull an existing out-of-area stop into their area, nor push an allowed stop beyond it.
        if (await _geoAuth.IsOutsideAreaAsync(userId, stop.Geom) ||
            await _geoAuth.IsOutsideAreaAsync(userId, destination))
            return StopWriteResult.OutsideAuthorizedArea;

        stop.Geom = destination;
        stop.ModifiedUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);

        // The valid position is committed before routing. If OSRM fails, RebuildRouteCoreAsync keeps
        // the prior line/metrics and marks them stale while this new stop point remains authoritative.
        var rebuild = await RebuildRouteCoreAsync(route, userId, cancellationToken);
        var status = rebuild.Status == TransportWriteStatus.InsufficientStops
            ? TransportWriteStatus.Success
            : rebuild.Status;
        return new StopWriteResult(
            status,
            ToStopResponse(stop, route, await UsernameAsync(stop.UserId)),
            rebuild.Route,
            StopPersisted: true);
    }

    public async Task<StopWriteResult> UpdateStopAsync(int id, StopUpdateRequest request, int userId)
    {
        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == id);
        if (stop is null)
            return StopWriteResult.StopNotFound;

        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == stop.RouteId);
        if (route is null)
            return StopWriteResult.RouteNotFound;

        // Stop-level, like placing or deleting one: a name/color edit is still a write to that stop.
        if (await _geoAuth.IsOutsideAreaAsync(userId, stop.Geom))
            return StopWriteResult.OutsideAuthorizedArea;

        stop.Name = request.Name.Trim();
        // Presentation only, like the name — clearing it back to null re-inherits the route color, and
        // neither field changes the stop's position, so this still never needs an OSRM rebuild.
        stop.Color = request.Color;
        stop.ModifiedUserId = userId;
        await _db.SaveChangesAsync();

        var stopCount = await _db.Stops.CountAsync(s => s.RouteId == route.Id);
        return StopWriteResult.Ok(
            ToStopResponse(stop, route, await UsernameAsync(stop.UserId)),
            ToRouteResponse(route, stopCount, await UsernameAsync(route.UserId)));
    }

    public async Task<DeleteStatus> DeleteRouteAsync(int id, int userId)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (route is null)
            return DeleteStatus.NotFound;

        // Checked before the cascade below: deleting a route takes every one of its stops with it, so
        // an area-restricted caller must be entitled to the whole route, not just part of it.
        if (await IsRouteOutsideAreaAsync(route, userId))
            return DeleteStatus.OutsideAuthorizedArea;
        if (_simulationStates.IsRunning(route.Id))
            return DeleteStatus.SimulationRunning;

        // Cascade to the stops in the same save. The module's invariant is that a live stop always has
        // a live route (ListAllStopsAsync relies on it), and the FK is Restrict, so leaving the stops
        // behind would strand rows that no query can reach.
        var stops = await _db.Stops.Where(s => s.RouteId == id).ToListAsync();
        foreach (var stop in stops)
        {
            stop.IsDeleted = true;
            stop.ModifiedUserId = userId;
        }

        route.IsDeleted = true;
        route.ModifiedUserId = userId;
        await _db.SaveChangesAsync();
        return DeleteStatus.Success;
    }

    public async Task<StopOrderResult> DeleteStopAsync(
        int id,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stop is null)
            return StopOrderResult.StopNotFound;

        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == stop.RouteId, cancellationToken);
        if (route is null)
            return StopOrderResult.RouteNotFound;
        if (_simulationStates.IsRunning(route.Id))
            return StopOrderResult.SimulationRunning;

        // A stop-level operation is bound by the stop's own position, symmetric with placing one.
        // Checked before the removal and renumbering below, which are committed ahead of the rebuild
        // and so must not run at all for a refused call.
        if (await _geoAuth.IsOutsideAreaAsync(userId, stop.Geom))
            return StopOrderResult.OutsideAuthorizedArea;

        stop.IsDeleted = true;
        stop.ModifiedUserId = userId;

        // SequenceOrder is 1..N with no gaps, so the survivors close the hole the removal leaves —
        // otherwise a route ends up numbered 1, 2, 4 on both the panel list and the map markers.
        var remaining = await _db.Stops
            .Where(s => s.RouteId == route.Id && s.Id != id)
            .OrderBy(s => s.SequenceOrder)
            .ToListAsync(cancellationToken);
        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].SequenceOrder = i + 1;
            remaining[i].ModifiedUserId = userId;
        }
        await _db.SaveChangesAsync(cancellationToken);

        // Committed before the network call, exactly like a reorder: a routing failure marks the route
        // stale but must never resurrect a stop the user deleted.
        var rebuild = await RebuildRouteCoreAsync(route, userId, cancellationToken);

        var usernames = await UsernameMapAsync(remaining.Select(s => s.UserId));
        var ordered = remaining
            .Select(s => ToStopResponse(s, route, usernames.GetValueOrDefault(s.UserId, string.Empty)))
            .ToList();
        var status = rebuild.Status == TransportWriteStatus.InsufficientStops
            ? TransportWriteStatus.Success
            : rebuild.Status;
        return new StopOrderResult(status, ordered, rebuild.Route, OrderPersisted: true);
    }

    public async Task<StopOrderResult> ReorderStopsAsync(
        int routeId,
        IReadOnlyList<int> orderedStopIds,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == routeId);
        if (route is null)
            return StopOrderResult.RouteNotFound;
        if (_simulationStates.IsRunning(routeId))
            return StopOrderResult.SimulationRunning;

        // Reordering renumbers every stop and rewrites the route's line, so it is route-level.
        if (await IsRouteOutsideAreaAsync(route, userId, cancellationToken))
            return StopOrderResult.OutsideAuthorizedArea;

        var stops = await _db.Stops.Where(s => s.RouteId == routeId).ToListAsync();

        // The submitted ids must be EXACTLY this route's current stops — same set, no repeats, no
        // strangers — or the reorder is rejected (a stale client, or a stop from another route).
        var currentIds = stops.Select(s => s.Id).ToHashSet();
        if (orderedStopIds.Count != currentIds.Count ||
            orderedStopIds.Distinct().Count() != orderedStopIds.Count ||
            !orderedStopIds.All(currentIds.Contains))
            return StopOrderResult.InvalidOrder;

        var byId = stops.ToDictionary(s => s.Id);
        for (var i = 0; i < orderedStopIds.Count; i++)
        {
            var stop = byId[orderedStopIds[i]];
            // Contiguous 1..N. The (route, order) index is non-unique, so renumbering row-by-row can
            // never trip a transient duplicate mid-save.
            stop.SequenceOrder = i + 1;
            stop.ModifiedUserId = userId;
        }
        await _db.SaveChangesAsync(cancellationToken);

        // The order is intentionally committed before the network call. A routing failure marks the
        // route stale but must never roll back the user's valid new order.
        var rebuild = await RebuildRouteCoreAsync(route, userId, cancellationToken);

        var usernames = await UsernameMapAsync(stops.Select(s => s.UserId));
        var ordered = orderedStopIds
            .Select(id => ToStopResponse(byId[id], route, usernames.GetValueOrDefault(byId[id].UserId, string.Empty)))
            .ToList();
        var status = rebuild.Status == TransportWriteStatus.InsufficientStops
            ? TransportWriteStatus.Success
            : rebuild.Status;
        return new StopOrderResult(status, ordered, rebuild.Route, OrderPersisted: true);
    }

    public async Task<RouteBuildResult> RebuildRouteAsync(
        int routeId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
        if (route is null)
            return RouteBuildResult.RouteNotFound;
        if (_simulationStates.IsRunning(routeId))
            return RouteBuildResult.SimulationRunning;

        // Rebuilding rewrites the route's line, distance and duration, so it is route-level too. Only
        // this public entry point checks: the internal RebuildRouteCoreAsync is also reached from the
        // stop-level paths, which have already applied their own (stop-scoped) rule.
        if (await IsRouteOutsideAreaAsync(route, userId, cancellationToken))
            return RouteBuildResult.OutsideAuthorizedArea;

        return await RebuildRouteCoreAsync(route, userId, cancellationToken);
    }

    private async Task<RouteBuildResult> RebuildRouteCoreAsync(
        TransportRoute route,
        int userId,
        CancellationToken cancellationToken)
    {
        var stops = await _db.Stops
            .Where(s => s.RouteId == route.Id)
            .OrderBy(s => s.SequenceOrder)
            .ToListAsync(cancellationToken);
        var username = await UsernameAsync(route.UserId);

        if (stops.Count < 2)
        {
            // A route that drops below two stops has nothing left to connect, so any line it still
            // carries runs to a stop that no longer exists. Clearing is only needed after a deletion —
            // a never-built route has no geometry — so the common case stays a pure read.
            if (route.Geometry is not null)
            {
                route.Geometry = null;
                route.DistanceMeters = null;
                route.DurationSeconds = null;
                route.IsGeometryStale = false;
                route.RoutingErrorCode = null;
                route.ModifiedUserId = userId;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return RouteBuildResult.From(
                TransportWriteStatus.InsufficientStops,
                ToRouteResponse(route, stops.Count, username));
        }

        var routing = await _routing.BuildRouteAsync(
            stops.Select(stop => stop.Geom.Coordinate).ToList(), cancellationToken);
        var status = routing.Status == RoutingStatus.Success && routing.Geometry is null
            ? TransportWriteStatus.RoutingUnavailable
            : MapRoutingStatus(routing.Status);

        if (status == TransportWriteStatus.Success)
        {
            route.Geometry = routing.Geometry!;
            route.DistanceMeters = routing.DistanceMeters;
            route.DurationSeconds = routing.DurationSeconds;
            route.IsGeometryStale = false;
            route.RoutingErrorCode = null;
        }
        else
        {
            // Geometry/distance/duration deliberately stay untouched: they are the last valid build.
            route.IsGeometryStale = route.Geometry is not null;
            route.RoutingErrorCode = RoutingErrorCode(status);
        }

        route.ModifiedUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);
        return RouteBuildResult.From(status, ToRouteResponse(route, stops.Count, username));
    }

    private static TransportWriteStatus MapRoutingStatus(RoutingStatus status) => status switch
    {
        RoutingStatus.Success => TransportWriteStatus.Success,
        RoutingStatus.NoRoute => TransportWriteStatus.NoRoute,
        RoutingStatus.InvalidCoordinates => TransportWriteStatus.InvalidCoordinates,
        _ => TransportWriteStatus.RoutingUnavailable,
    };

    public static string? RoutingErrorCode(TransportWriteStatus status) => status switch
    {
        TransportWriteStatus.NoRoute => "no_route",
        TransportWriteStatus.InvalidCoordinates => "invalid_coordinates",
        TransportWriteStatus.RoutingUnavailable => "routing_unavailable",
        _ => null,
    };

    // Usernames for a set of creator ids. IgnoreQueryFilters so a soft-deleted creator's name still
    // resolves (mirrors the POI list). The id list is a concrete array, so EF translates Contains.
    private async Task<Dictionary<int, string>> UsernameMapAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, string>();
        return await _db.Users.IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username);
    }

    private async Task<string> UsernameAsync(int userId) =>
        await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync() ?? string.Empty;

    private static RouteResponse ToRouteResponse(TransportRoute route, int stopCount, string createdBy) =>
        new()
        {
            Id = route.Id,
            Name = route.Name,
            Color = route.Color,
            GeometryWkt = route.Geometry?.AsText(),
            DistanceMeters = route.DistanceMeters,
            DurationSeconds = route.DurationSeconds,
            IsGeometryStale = route.IsGeometryStale,
            RoutingErrorCode = route.RoutingErrorCode,
            StopCount = stopCount,
            UserId = route.UserId,
            CreatedBy = createdBy,
            CreatedAt = route.CreatedAt,
            ModifiedDate = route.ModifiedDate,
        };

    private static StopResponse ToStopResponse(Stop stop, TransportRoute route, string createdBy) =>
        new()
        {
            Id = stop.Id,
            Wkt = stop.Geom.AsText(),
            Name = stop.Name ?? string.Empty,
            RouteId = stop.RouteId,
            RouteName = route.Name,
            RouteColor = route.Color,
            Color = stop.Color,
            SequenceOrder = stop.SequenceOrder,
            UserId = stop.UserId,
            CreatedBy = createdBy,
            CreatedAt = stop.CreatedAt,
            ModifiedDate = stop.ModifiedDate,
        };
}
