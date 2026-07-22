using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// Outcomes of the transportation writes, mirroring PoiWriteResult's shape so the controllers map each
// case the same way. One status enum is shared; a small result record per return shape carries the
// payload (a route, a stop, or the reordered stop list).
public enum TransportWriteStatus
{
    Success,
    InvalidGeometry,      // stop WKT was not a single valid point (400)
    RouteNotFound,        // referenced route does not exist (400 on create, 404 on a route-scoped path)
    DuplicateName,        // another live route already uses that name (409)
    OutsideAuthorizedArea,// stop fell outside the caller's authorized area (403)
    InvalidOrder,         // reorder ids don't match the route's current stops (400)
    StopNotFound,         // admin stop update target does not exist (404)
    InsufficientStops,    // a route with fewer than two stops cannot be built (409)
    NoRoute,              // OSRM could not connect the ordered waypoints (422)
    InvalidCoordinates,   // a waypoint is invalid or cannot snap to the road graph (422)
    RoutingUnavailable,   // configured OSRM services failed transiently (503)
}

public record RouteWriteResult(TransportWriteStatus Status, RouteResponse? Response)
{
    public static readonly RouteWriteResult NotFound = new(TransportWriteStatus.RouteNotFound, null);
    public static readonly RouteWriteResult DuplicateName = new(TransportWriteStatus.DuplicateName, null);
    public static RouteWriteResult Ok(RouteResponse response) => new(TransportWriteStatus.Success, response);
}

public record StopWriteResult(
    TransportWriteStatus Status,
    StopResponse? Response,
    RouteResponse? Route = null,
    bool StopPersisted = false)
{
    public static readonly StopWriteResult InvalidGeometry = new(TransportWriteStatus.InvalidGeometry, null);
    public static readonly StopWriteResult RouteNotFound = new(TransportWriteStatus.RouteNotFound, null);
    public static readonly StopWriteResult StopNotFound = new(TransportWriteStatus.StopNotFound, null);
    public static readonly StopWriteResult OutsideAuthorizedArea = new(TransportWriteStatus.OutsideAuthorizedArea, null);
    public static StopWriteResult Ok(StopResponse response, RouteResponse route) =>
        new(TransportWriteStatus.Success, response, route, true);
}

// Shared by reorder and stop deletion: both renumber a route's stops to 1..N and rebuild its geometry,
// so both answer with the route's surviving stops in order. OrderPersisted means the renumbering is
// committed — true even when the OSRM rebuild that followed it failed.
public record StopOrderResult(
    TransportWriteStatus Status,
    IReadOnlyList<StopResponse>? Stops,
    RouteResponse? Route = null,
    bool OrderPersisted = false)
{
    public static readonly StopOrderResult RouteNotFound = new(TransportWriteStatus.RouteNotFound, null);
    public static readonly StopOrderResult InvalidOrder = new(TransportWriteStatus.InvalidOrder, null);
    public static readonly StopOrderResult StopNotFound = new(TransportWriteStatus.StopNotFound, null);
    public static StopOrderResult Ok(IReadOnlyList<StopResponse> stops, RouteResponse route) =>
        new(TransportWriteStatus.Success, stops, route, true);
}

public record RouteBuildResult(TransportWriteStatus Status, RouteResponse? Route)
{
    public static readonly RouteBuildResult RouteNotFound = new(TransportWriteStatus.RouteNotFound, null);
    public static RouteBuildResult From(TransportWriteStatus status, RouteResponse route) => new(status, route);
}
