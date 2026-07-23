using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/routes/{routeId:int}/simulation")]
public class RouteSimulationsController : ControllerBase
{
    private readonly IRouteSimulationService _simulations;
    private readonly IUserAdminService _users;
    private readonly ILogger<RouteSimulationsController> _logger;

    public RouteSimulationsController(
        IRouteSimulationService simulations,
        IUserAdminService users,
        ILogger<RouteSimulationsController> logger)
    {
        _simulations = simulations;
        _users = users;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<RouteSimulationResponse>> Get(
        int routeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _simulations.GetAsync(routeId, cancellationToken);
            return result.Status == RouteSimulationOperationStatus.RouteNotFound
                ? NotFound(new { message = "Route not found.", code = "route_not_found" })
                : Ok(result.State);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Get)); }
    }

    [HttpPost("start")]
    public async Task<ActionResult<RouteSimulationResponse>> Start(
        int routeId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await CanControlAsync(userId))
                return Forbid();
            return Map(await _simulations.StartAsync(routeId, userId, cancellationToken));
        }
        catch (Exception ex) { return ServerError(ex, nameof(Start)); }
    }

    [HttpPost("stop")]
    public async Task<ActionResult<RouteSimulationResponse>> Stop(
        int routeId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await CanControlAsync(userId))
                return Forbid();
            return Map(await _simulations.StopAsync(routeId, cancellationToken));
        }
        catch (Exception ex) { return ServerError(ex, nameof(Stop)); }
    }

    [HttpPost("resume")]
    public async Task<ActionResult<RouteSimulationResponse>> Resume(
        int routeId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await CanControlAsync(userId))
                return Forbid();
            return Map(await _simulations.ResumeAsync(routeId, cancellationToken));
        }
        catch (Exception ex) { return ServerError(ex, nameof(Resume)); }
    }

    [HttpPost("end")]
    public async Task<ActionResult<RouteSimulationResponse>> End(
        int routeId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await CanControlAsync(userId))
                return Forbid();
            return Map(await _simulations.EndAsync(routeId, cancellationToken));
        }
        catch (Exception ex) { return ServerError(ex, nameof(End)); }
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private async Task<bool> CanControlAsync(int userId) =>
        await _users.HasPermissionAsync(userId, SeedData.ManageTransportPermission) ||
        await _users.IsAdminAsync(userId);

    private ActionResult<RouteSimulationResponse> Map(RouteSimulationOperationResult result) =>
        result.Status switch
        {
            RouteSimulationOperationStatus.RouteNotFound =>
                NotFound(new { message = "Route not found.", code = "route_not_found" }),
            RouteSimulationOperationStatus.OutsideAuthorizedArea => StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = RoutesController.RouteOutsideAreaMessage, code = "outside_authorized_area" }),
            RouteSimulationOperationStatus.InsufficientStops => Conflict(new
            {
                message = "At least two stops are required to simulate a route.",
                code = "insufficient_stops",
            }),
            RouteSimulationOperationStatus.GeometryMissing => UnprocessableEntity(new
            {
                message = "Build the route before starting a simulation.",
                code = "route_geometry_missing",
            }),
            RouteSimulationOperationStatus.GeometryStale => UnprocessableEntity(new
            {
                message = "Rebuild the stale route before starting a simulation.",
                code = "stale_route_geometry",
            }),
            RouteSimulationOperationStatus.InvalidGeometry => UnprocessableEntity(new
            {
                message = "The persisted route geometry is invalid.",
                code = "invalid_route_geometry",
            }),
            RouteSimulationOperationStatus.SimulationAlreadyRunning => Conflict(new
            {
                message = "A simulation is already running for this route.",
                code = "simulation_already_running",
                state = result.State,
            }),
            RouteSimulationOperationStatus.SimulationNotRunning => Conflict(new
            {
                message = "No simulation is running for this route.",
                code = "simulation_not_running",
                state = result.State,
            }),
            _ => Ok(result.State),
        };

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in RouteSimulationsController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }
}
