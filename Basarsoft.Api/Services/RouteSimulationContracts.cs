using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public enum RouteSimulationOperationStatus
{
    Success,
    RouteNotFound,
    OutsideAuthorizedArea,
    InsufficientStops,
    GeometryMissing,
    GeometryStale,
    InvalidGeometry,
    SimulationAlreadyRunning,
    SimulationNotRunning,
}

public record RouteSimulationOperationResult(
    RouteSimulationOperationStatus Status,
    RouteSimulationResponse? State = null);

public interface IRouteSimulationService
{
    Task<RouteSimulationOperationResult> GetAsync(int routeId, CancellationToken cancellationToken = default);
    Task<RouteSimulationOperationResult> StartAsync(
        int routeId,
        int userId,
        CancellationToken cancellationToken = default);
    Task<RouteSimulationOperationResult> StopAsync(
        int routeId,
        CancellationToken cancellationToken = default);
    Task<RouteSimulationOperationResult> ResumeAsync(
        int routeId,
        CancellationToken cancellationToken = default);
    // Clears any run for the route back to NotStarted (first-time state). Idempotent: a route with no
    // run just reports NotStarted. Distinct from Stop, which freezes a run so it can be resumed.
    Task<RouteSimulationOperationResult> EndAsync(
        int routeId,
        CancellationToken cancellationToken = default);
}

// The transportation write service only needs this narrow read contract. A future persistent or
// distributed store can replace the in-memory implementation without changing CRUD behavior.
public interface IRouteSimulationStateReader
{
    bool IsRunning(int routeId);
}

public interface IRouteSimulationPublisher
{
    Task PublishAsync(RouteSimulationResponse state, CancellationToken cancellationToken = default);
}

public record RouteSimulationStopSnapshot(string? Name, double Longitude, double Latitude);

public record RouteSimulationRouteSnapshot(
    int RouteId,
    IReadOnlyList<(double Longitude, double Latitude)> GeometryCoordinates,
    IReadOnlyList<RouteSimulationStopSnapshot> Stops,
    double? DistanceMeters,
    double? DurationSeconds);

public record RouteSimulationSnapshotResult(
    RouteSimulationOperationStatus Status,
    RouteSimulationRouteSnapshot? Snapshot = null);

public interface IRouteSimulationRouteLoader
{
    Task<bool> ExistsAsync(int routeId, CancellationToken cancellationToken = default);
    Task<RouteSimulationSnapshotResult> LoadForStartAsync(
        int routeId,
        int userId,
        CancellationToken cancellationToken = default);
}
