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
}

public record RouteWriteResult(TransportWriteStatus Status, RouteResponse? Response)
{
    public static readonly RouteWriteResult NotFound = new(TransportWriteStatus.RouteNotFound, null);
    public static readonly RouteWriteResult DuplicateName = new(TransportWriteStatus.DuplicateName, null);
    public static RouteWriteResult Ok(RouteResponse response) => new(TransportWriteStatus.Success, response);
}

public record StopWriteResult(TransportWriteStatus Status, StopResponse? Response)
{
    public static readonly StopWriteResult InvalidGeometry = new(TransportWriteStatus.InvalidGeometry, null);
    public static readonly StopWriteResult RouteNotFound = new(TransportWriteStatus.RouteNotFound, null);
    public static readonly StopWriteResult OutsideAuthorizedArea = new(TransportWriteStatus.OutsideAuthorizedArea, null);
    public static StopWriteResult Ok(StopResponse response) => new(TransportWriteStatus.Success, response);
}

public record StopOrderResult(TransportWriteStatus Status, IReadOnlyList<StopResponse>? Stops)
{
    public static readonly StopOrderResult RouteNotFound = new(TransportWriteStatus.RouteNotFound, null);
    public static readonly StopOrderResult InvalidOrder = new(TransportWriteStatus.InvalidOrder, null);
    public static StopOrderResult Ok(IReadOnlyList<StopResponse> stops) => new(TransportWriteStatus.Success, stops);
}
