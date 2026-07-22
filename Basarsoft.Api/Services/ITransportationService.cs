using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// Reads and writes for the transportation module (routes + their ordered stops). Reads stay on EF and
// are open to any authenticated user; writes are gated by the manage_transport permission in the
// controllers. Nothing here goes through GeoServer — stops render from a plain client vector layer.
public interface ITransportationService
{
    // All routes, id order, each with its live stop count.
    Task<IReadOnlyList<RouteResponse>> ListRoutesAsync();

    // Creates a route owned by userId. DuplicateName when another live route already uses the name.
    Task<RouteWriteResult> CreateRouteAsync(RouteSaveRequest request, int userId);

    // Renames/recolors a route. NotFound when it doesn't exist; DuplicateName on a name clash.
    Task<RouteWriteResult> UpdateRouteAsync(int id, RouteSaveRequest request, int userId);

    // A route's stops in sequence order. Null when the route doesn't exist (so the controller can 404),
    // an empty list when it exists but has no stops.
    Task<IReadOnlyList<StopResponse>?> ListRouteStopsAsync(int routeId);

    // Every stop in the system (route + sequence order), for the map layer.
    Task<IReadOnlyList<StopResponse>> ListAllStopsAsync();

    // Creates a stop at the drawn point, appended to the end of its route (SequenceOrder = max + 1).
    // Validates the WKT is a point, the route exists, and the point is inside the caller's area.
    Task<StopWriteResult> CreateStopAsync(
        StopCreateRequest request,
        int userId,
        CancellationToken cancellationToken = default);

    // Moves one stop after checking both its current and destination points against the caller's
    // effective area. The point commits before the route rebuild, matching create/reorder semantics.
    Task<StopWriteResult> MoveStopAsync(
        int id,
        StopMoveRequest request,
        int userId,
        CancellationToken cancellationToken = default);

    // Admin-only metadata update; operational relocation stays a separate position-only call.
    Task<StopWriteResult> UpdateStopAsync(int id, StopUpdateRequest request, int userId);

    // Soft-deletes the route and, in the same save, every stop on it — a live stop must always have a
    // live route. NotFound when the route doesn't exist; OutsideAuthorizedArea when an area-restricted
    // caller is not entitled to the whole route (its line, or its stops when it has never been built).
    Task<DeleteStatus> DeleteRouteAsync(int id, int userId);

    // Soft-deletes one stop, renumbers its route's survivors back to 1..N, and rebuilds the geometry.
    // Returns a reorder's shape (remaining stops + route) so one round trip refreshes the whole panel.
    Task<StopOrderResult> DeleteStopAsync(
        int id,
        int userId,
        CancellationToken cancellationToken = default);

    // Renumbers a route's stops to the submitted order (1..N). InvalidOrder when the id set doesn't
    // match the route's current stops exactly; RouteNotFound when the route doesn't exist.
    Task<StopOrderResult> ReorderStopsAsync(
        int routeId,
        IReadOnlyList<int> orderedStopIds,
        int userId,
        CancellationToken cancellationToken = default);

    // One shared OSRM rebuild used by explicit build, stop creation, operational reorder, and admin.
    Task<RouteBuildResult> RebuildRouteAsync(
        int routeId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<AdminTransportationResponse> GetAdminSnapshotAsync();
}
