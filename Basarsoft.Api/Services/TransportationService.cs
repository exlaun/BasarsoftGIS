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
    private readonly WKTReader _wktReader = new();

    // Same storage CRS as every other geometry table: EPSG:4326 (WGS84 lon-lat).
    private const int Srid = 4326;

    public TransportationService(AppDbContext db, IGeoAuthorizationService geoAuth)
    {
        _db = db;
        _geoAuth = geoAuth;
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

    public async Task<RouteWriteResult> UpdateRouteAsync(int id, RouteSaveRequest request, int userId)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (route is null)
            return RouteWriteResult.NotFound;

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

    public async Task<StopWriteResult> CreateStopAsync(StopCreateRequest request, int userId)
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

        // Same geographic authorization as every other draw tool: with an assigned area, stops may
        // only be placed inside it (Covers keeps boundary points legal).
        var area = await _geoAuth.GetEffectiveAreaAsync(userId);
        if (area is not null && !area.Covers(geom))
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
        await _db.SaveChangesAsync();

        return StopWriteResult.Ok(ToStopResponse(stop, route, await UsernameAsync(userId)));
    }

    public async Task<StopOrderResult> ReorderStopsAsync(int routeId, IReadOnlyList<int> orderedStopIds, int userId)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == routeId);
        if (route is null)
            return StopOrderResult.RouteNotFound;

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
        await _db.SaveChangesAsync();

        var usernames = await UsernameMapAsync(stops.Select(s => s.UserId));
        var ordered = orderedStopIds
            .Select(id => ToStopResponse(byId[id], route, usernames.GetValueOrDefault(byId[id].UserId, string.Empty)))
            .ToList();
        return StopOrderResult.Ok(ordered);
    }

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
            SequenceOrder = stop.SequenceOrder,
            UserId = stop.UserId,
            CreatedBy = createdBy,
            CreatedAt = stop.CreatedAt,
            ModifiedDate = stop.ModifiedDate,
        };
}
