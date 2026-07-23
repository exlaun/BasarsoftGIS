using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// Transportation stops (points belonging to a route). GET is open to every authenticated user (it
// feeds the map's stop layer); creating, relocating, and deleting a stop require manage_transport.
// Relocation changes position only; admin may edit name/color, and no endpoint moves stops between
// routes. Reordering lives in RoutesController. Reads stay on EF.
[ApiController]
[Authorize]
[Route("api/stops")]
public class StopsController : ControllerBase
{
    private readonly ITransportationService _transport;
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<StopsController> _logger;

    public StopsController(
        ITransportationService transport,
        IUserAdminService userAdminService,
        ILogger<StopsController> logger)
    {
        _transport = transport;
        _userAdminService = userAdminService;
        _logger = logger;
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in StopsController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    // GET /api/stops -> every stop in the system (route + sequence order). Feeds the map layer;
    // open to any authenticated user, so End Users see stops on the map.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StopResponse>>> List()
    {
        try { return Ok(await _transport.ListAllStopsAsync()); }
        catch (Exception ex) { return ServerError(ex, nameof(List)); }
    }

    // POST /api/stops -> add a stop at the drawn point, appended to its route. Requires manage_transport.
    [HttpPost]
    public async Task<ActionResult<StopCreateResponse>> Create(
        StopCreateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await _userAdminService.HasPermissionAsync(userId, SeedData.ManageTransportPermission))
                return Forbid();

            var result = await _transport.CreateStopAsync(request, userId, cancellationToken);
            return result.Status switch
            {
                TransportWriteStatus.InvalidGeometry =>
                    BadRequest(new { message = "WKT must be a single valid point." }),
                TransportWriteStatus.RouteNotFound =>
                    BadRequest(new { message = "Route not found.", code = "route_not_found" }),
                // The `code` field lets the client tell this 403 apart from the permission 403 above.
                TransportWriteStatus.OutsideAuthorizedArea => StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "The stop is outside your authorized area.", code = "outside_authorized_area" }),
                TransportWriteStatus.SimulationRunning => SimulationConflict(),
                TransportWriteStatus.NoRoute or TransportWriteStatus.InvalidCoordinates =>
                    UnprocessableEntity(new
                    {
                        message = RoutingMessage(result.Status),
                        code = TransportationService.RoutingErrorCode(result.Status),
                        stopPersisted = result.StopPersisted,
                        stop = result.Response,
                        route = result.Route,
                    }),
                TransportWriteStatus.RoutingUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        message = RoutingMessage(result.Status),
                        code = TransportationService.RoutingErrorCode(result.Status),
                        stopPersisted = result.StopPersisted,
                        stop = result.Response,
                        route = result.Route,
                    }),
                _ => Ok(new StopCreateResponse { Stop = result.Response!, Route = result.Route! }),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex) { return ServerError(ex, nameof(Create)); }
    }

    // PUT /api/stops/{id}/location -> persist a new point and rebuild that stop's route. The location
    // commits first, so routing failures carry the authoritative stop/route state back to the map.
    [HttpPut("{id:int}/location")]
    public async Task<ActionResult<StopCreateResponse>> Move(
        int id,
        StopMoveRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await _userAdminService.HasPermissionAsync(userId, SeedData.ManageTransportPermission))
                return Forbid();

            var result = await _transport.MoveStopAsync(id, request, userId, cancellationToken);
            return result.Status switch
            {
                TransportWriteStatus.InvalidGeometry =>
                    BadRequest(new { message = "WKT must be a single valid point." }),
                TransportWriteStatus.StopNotFound => NotFound(new { message = "Stop not found." }),
                TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
                TransportWriteStatus.OutsideAuthorizedArea => StatusCode(StatusCodes.Status403Forbidden,
                    new
                    {
                        message = "The stop's current and new locations must be inside your authorized area.",
                        code = "outside_authorized_area",
                    }),
                TransportWriteStatus.SimulationRunning => SimulationConflict(),
                TransportWriteStatus.NoRoute or TransportWriteStatus.InvalidCoordinates =>
                    UnprocessableEntity(MoveError(result)),
                TransportWriteStatus.RoutingUnavailable =>
                    StatusCode(StatusCodes.Status503ServiceUnavailable, MoveError(result)),
                _ => Ok(new StopCreateResponse { Stop = result.Response!, Route = result.Route! }),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex) { return ServerError(ex, nameof(Move)); }
    }

    // DELETE /api/stops/{id} -> remove a stop, renumber its route, rebuild geometry. Requires
    // manage_transport. Answers with the route's surviving stops plus the route itself so the caller
    // refreshes its list and the map line in one round trip instead of refetching both.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult<RouteStopsResponse>> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await _userAdminService.HasPermissionAsync(userId, SeedData.ManageTransportPermission))
                return Forbid();

            var result = await _transport.DeleteStopAsync(id, userId, cancellationToken);
            return result.Status switch
            {
                TransportWriteStatus.StopNotFound => NotFound(new { message = "Stop not found." }),
                TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
                // Refused before anything was committed, so unlike the routing failures below this
                // carries no partial-success payload.
                TransportWriteStatus.OutsideAuthorizedArea => StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "That stop is outside your authorized area.", code = "outside_authorized_area" }),
                TransportWriteStatus.SimulationRunning => SimulationConflict(),
                // The deletion itself is already committed, so these carry deletePersisted plus the
                // fresh state — the same partial-success contract a failed reorder returns.
                TransportWriteStatus.NoRoute or TransportWriteStatus.InvalidCoordinates =>
                    UnprocessableEntity(DeleteError(result)),
                TransportWriteStatus.RoutingUnavailable =>
                    StatusCode(StatusCodes.Status503ServiceUnavailable, DeleteError(result)),
                _ => Ok(new RouteStopsResponse { Stops = result.Stops!, Route = result.Route! }),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex) { return ServerError(ex, nameof(Delete)); }
    }

    private static object DeleteError(StopOrderResult result) => new
    {
        message = DeleteRoutingMessage(result.Status),
        code = TransportationService.RoutingErrorCode(result.Status),
        deletePersisted = result.OrderPersisted,
        stops = result.Stops,
        route = result.Route,
    };

    private static object MoveError(StopWriteResult result) => new
    {
        message = MoveRoutingMessage(result.Status),
        code = TransportationService.RoutingErrorCode(result.Status),
        locationPersisted = result.StopPersisted,
        stop = result.Response,
        route = result.Route,
    };

    private static string MoveRoutingMessage(TransportWriteStatus status) => status switch
    {
        TransportWriteStatus.NoRoute => "The stop was relocated, but no road route connects the ordered stops.",
        TransportWriteStatus.InvalidCoordinates => "The stop was relocated, but a stop has invalid routing coordinates.",
        _ => "The stop was relocated, but routing is unavailable; previous route geometry was preserved.",
    };

    private static string DeleteRoutingMessage(TransportWriteStatus status) => status switch
    {
        TransportWriteStatus.NoRoute => "The stop was deleted, but no road route connects the remaining stops.",
        TransportWriteStatus.InvalidCoordinates => "The stop was deleted, but a remaining stop has invalid routing coordinates.",
        _ => "The stop was deleted, but routing is unavailable; previous route geometry was preserved.",
    };

    private static string RoutingMessage(TransportWriteStatus status) => status switch
    {
        TransportWriteStatus.NoRoute => "The stop was saved, but no road route connects the ordered stops.",
        TransportWriteStatus.InvalidCoordinates => "The stop was saved, but a stop has invalid routing coordinates.",
        _ => "The stop was saved, but routing is unavailable; previous route geometry was preserved.",
    };

    private ObjectResult SimulationConflict() => Conflict(new
    {
        message = "Stop the running simulation before changing this route's stops.",
        code = "simulation_running",
    });
}
