using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Security;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

[ApiController]
[Authorize(Policy = PermissionRequirement.ManageTransportAdmin)]
[Route("api/admin/transportation")]
public class AdminTransportationController : ControllerBase
{
    private readonly ITransportationService _transport;
    private readonly ILogger<AdminTransportationController> _logger;

    public AdminTransportationController(
        ITransportationService transport,
        ILogger<AdminTransportationController> logger)
    {
        _transport = transport;
        _logger = logger;
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in AdminTransportationController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    [HttpGet]
    public async Task<ActionResult<AdminTransportationResponse>> List()
    {
        try { return Ok(await _transport.GetAdminSnapshotAsync()); }
        catch (Exception ex) { return ServerError(ex, nameof(List)); }
    }

    [HttpPut("routes/{id:int}")]
    public async Task<ActionResult<RouteResponse>> UpdateRoute(int id, RouteSaveRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var result = await _transport.UpdateRouteAsync(id, request, userId);
            return result.Status switch
            {
                TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
                TransportWriteStatus.OutsideAuthorizedArea => OutsideArea(RoutesController.RouteOutsideAreaMessage),
                TransportWriteStatus.DuplicateName => Conflict(new
                {
                    message = "A route with that name already exists.",
                    code = "duplicate_name",
                }),
                _ => Ok(result.Response),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(UpdateRoute)); }
    }

    [HttpPut("stops/{id:int}")]
    public async Task<ActionResult<StopResponse>> UpdateStop(int id, StopUpdateRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var result = await _transport.UpdateStopAsync(id, request, userId);
            return result.Status switch
            {
                TransportWriteStatus.StopNotFound => NotFound(new { message = "Stop not found." }),
                TransportWriteStatus.OutsideAuthorizedArea => OutsideArea(StopOutsideAreaMessage),
                _ => Ok(result.Response),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(UpdateStop)); }
    }

    // Deleting a route takes its stops with it — see TransportationService.DeleteRouteAsync.
    [HttpDelete("routes/{id:int}")]
    public async Task<ActionResult> DeleteRoute(int id)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return await _transport.DeleteRouteAsync(id, userId) switch
            {
                DeleteStatus.NotFound => NotFound(new { message = "Route not found." }),
                DeleteStatus.OutsideAuthorizedArea => OutsideArea(RoutesController.RouteOutsideAreaMessage),
                _ => NoContent(),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(DeleteRoute)); }
    }

    // Answers with the route's surviving (renumbered) stops plus the route, like the operational
    // endpoint, so the admin table reconciles through the same path a reorder already uses.
    [HttpDelete("stops/{id:int}")]
    public async Task<ActionResult<RouteStopsResponse>> DeleteStop(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var result = await _transport.DeleteStopAsync(id, userId, cancellationToken);
            return result.Status switch
            {
                TransportWriteStatus.StopNotFound => NotFound(new { message = "Stop not found." }),
                TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
                TransportWriteStatus.OutsideAuthorizedArea => OutsideArea(StopOutsideAreaMessage),
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
        catch (Exception ex) { return ServerError(ex, nameof(DeleteStop)); }
    }

    [HttpPut("routes/{id:int}/stops/order")]
    public async Task<ActionResult<RouteStopsResponse>> ReorderStops(
        int id,
        StopOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var result = await _transport.ReorderStopsAsync(
                id, request.OrderedStopIds, userId, cancellationToken);
            return MapOrderResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex) { return ServerError(ex, nameof(ReorderStops)); }
    }

    [HttpPost("routes/{id:int}/build")]
    public async Task<ActionResult<RouteResponse>> BuildRoute(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return MapBuildResult(await _transport.RebuildRouteAsync(id, userId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex) { return ServerError(ex, nameof(BuildRoute)); }
    }

    // Stop-level writes are bound by the stop's own position; route-level ones by the whole route.
    private const string StopOutsideAreaMessage = "That stop is outside your authorized area.";

    private ObjectResult OutsideArea(string message) => StatusCode(
        StatusCodes.Status403Forbidden, new { message, code = "outside_authorized_area" });

    private ActionResult<RouteStopsResponse> MapOrderResult(StopOrderResult result) => result.Status switch
    {
        TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
        TransportWriteStatus.OutsideAuthorizedArea => OutsideArea(RoutesController.RouteOutsideAreaMessage),
        TransportWriteStatus.InvalidOrder => BadRequest(new
        {
            message = "The submitted stop order doesn't match this route's stops.",
            code = "invalid_order",
        }),
        TransportWriteStatus.NoRoute or TransportWriteStatus.InvalidCoordinates =>
            UnprocessableEntity(PartialOrderError(result)),
        TransportWriteStatus.RoutingUnavailable =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, PartialOrderError(result)),
        _ => Ok(new RouteStopsResponse { Stops = result.Stops!, Route = result.Route! }),
    };

    private ActionResult<RouteResponse> MapBuildResult(RouteBuildResult result) => result.Status switch
    {
        TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
        TransportWriteStatus.OutsideAuthorizedArea => OutsideArea(RoutesController.RouteOutsideAreaMessage),
        TransportWriteStatus.InsufficientStops => Conflict(new
        {
            message = "At least two stops are required to build a route.",
            code = "insufficient_stops",
            route = result.Route,
        }),
        TransportWriteStatus.NoRoute or TransportWriteStatus.InvalidCoordinates =>
            UnprocessableEntity(BuildError(result)),
        TransportWriteStatus.RoutingUnavailable =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, BuildError(result)),
        _ => Ok(result.Route),
    };

    private static object PartialOrderError(StopOrderResult result) => new
    {
        message = RoutingMessage(result.Status),
        code = TransportationService.RoutingErrorCode(result.Status),
        orderPersisted = result.OrderPersisted,
        stops = result.Stops,
        route = result.Route,
    };

    // Same envelope, different flag name: the client has to know the deletion stuck even though the
    // rebuild that followed it did not.
    private static object DeleteError(StopOrderResult result) => new
    {
        message = RoutingMessage(result.Status),
        code = TransportationService.RoutingErrorCode(result.Status),
        deletePersisted = result.OrderPersisted,
        stops = result.Stops,
        route = result.Route,
    };

    private static object BuildError(RouteBuildResult result) => new
    {
        message = RoutingMessage(result.Status),
        code = TransportationService.RoutingErrorCode(result.Status),
        route = result.Route,
    };

    private static string RoutingMessage(TransportWriteStatus status) => status switch
    {
        TransportWriteStatus.NoRoute => "No road route connects the ordered stops.",
        TransportWriteStatus.InvalidCoordinates => "One or more stops have invalid routing coordinates.",
        _ => "Routing services are currently unavailable; the previous route geometry was preserved.",
    };
}
