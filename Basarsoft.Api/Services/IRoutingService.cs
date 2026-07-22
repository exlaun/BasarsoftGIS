using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Services;

public enum RoutingStatus
{
    Success,
    NoRoute,
    InvalidCoordinates,
    Unavailable,
}

public record RoutingResult(
    RoutingStatus Status,
    LineString? Geometry = null,
    double? DistanceMeters = null,
    double? DurationSeconds = null)
{
    public static readonly RoutingResult NoRoute = new(RoutingStatus.NoRoute);
    public static readonly RoutingResult InvalidCoordinates = new(RoutingStatus.InvalidCoordinates);
    public static readonly RoutingResult Unavailable = new(RoutingStatus.Unavailable);

    public static RoutingResult Ok(LineString geometry, double distanceMeters, double durationSeconds) =>
        new(RoutingStatus.Success, geometry, distanceMeters, durationSeconds);
}

public interface IRoutingService
{
    Task<RoutingResult> BuildRouteAsync(
        IReadOnlyList<Coordinate> orderedCoordinates,
        CancellationToken cancellationToken = default);
}
