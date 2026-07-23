using Basarsoft.Api.Data;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Services;

// Opens a short DI scope for each start/read because the simulation runtime is singleton while EF's
// DbContext and geographic authorization service are scoped. The returned snapshot owns plain values
// and remains safe after the scope is disposed.
public sealed class RouteSimulationRouteLoader : IRouteSimulationRouteLoader
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RouteSimulationRouteLoader(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> ExistsAsync(int routeId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Routes.AnyAsync(route => route.Id == routeId, cancellationToken);
    }

    public async Task<RouteSimulationSnapshotResult> LoadForStartAsync(
        int routeId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var route = await db.Routes.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == routeId, cancellationToken);
        if (route is null)
            return new(RouteSimulationOperationStatus.RouteNotFound);

        var stops = await db.Stops.AsNoTracking()
            .Where(stop => stop.RouteId == routeId)
            .OrderBy(stop => stop.SequenceOrder)
            .ToListAsync(cancellationToken);
        if (stops.Count < 2)
            return new(RouteSimulationOperationStatus.InsufficientStops);
        if (route.Geometry is null)
            return new(RouteSimulationOperationStatus.GeometryMissing);
        if (route.IsGeometryStale)
            return new(RouteSimulationOperationStatus.GeometryStale);
        if (route.Geometry.IsEmpty || !route.Geometry.IsValid || route.Geometry.NumPoints < 2)
            return new(RouteSimulationOperationStatus.InvalidGeometry);

        var geoAuth = scope.ServiceProvider.GetRequiredService<IGeoAuthorizationService>();
        var area = await geoAuth.GetEffectiveAreaAsync(userId);
        if (area is not null && !area.Covers(route.Geometry))
            return new(RouteSimulationOperationStatus.OutsideAuthorizedArea);

        try
        {
            var geometryCoordinates = route.Geometry.Coordinates
                .Select(coordinate => (coordinate.X, coordinate.Y))
                .ToArray();
            var stopSnapshots = stops.Select(stop =>
            {
                if (stop.Geom is not Point point)
                    throw new InvalidOperationException("A transportation stop is not a point.");
                return new RouteSimulationStopSnapshot(stop.Name, point.X, point.Y);
            }).ToArray();

            return new(
                RouteSimulationOperationStatus.Success,
                new RouteSimulationRouteSnapshot(
                    routeId,
                    geometryCoordinates,
                    stopSnapshots,
                    route.DistanceMeters,
                    route.DurationSeconds));
        }
        catch
        {
            return new(RouteSimulationOperationStatus.InvalidGeometry);
        }
    }
}
