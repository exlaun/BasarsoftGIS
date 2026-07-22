using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// Transportation routes (name + color) and the ordered stops that belong to each. Reading is open to
// every authenticated user (End Users browse routes read-only); creating/editing routes and reordering
// their stops require the manage_transport permission (Operators). Stops themselves are created via
// StopsController. Reads stay on EF — nothing here touches GeoServer.
[ApiController]
[Authorize]
[Route("api/routes")]
public class RoutesController : ControllerBase
{
    private readonly ITransportationService _transport;
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<RoutesController> _logger;

    public RoutesController(
        ITransportationService transport,
        IUserAdminService userAdminService,
        ILogger<RoutesController> logger)
    {
        _transport = transport;
        _userAdminService = userAdminService;
        _logger = logger;
    }

    // The logged-in user's id from the JWT "sub" claim (same idiom as PoiController).
    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in RoutesController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    // GET /api/routes -> every route with its live stop count (shared data, any authenticated user).
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RouteResponse>>> List()
    {
        try { return Ok(await _transport.ListRoutesAsync()); }
        catch (Exception ex) { return ServerError(ex, nameof(List)); }
    }

    // GET /api/routes/{id}/stops -> the route's stops in sequence order (any authenticated user).
    [HttpGet("{id:int}/stops")]
    public async Task<ActionResult<IReadOnlyList<StopResponse>>> Stops(int id)
    {
        try
        {
            var stops = await _transport.ListRouteStopsAsync(id);
            if (stops is null)
                return NotFound(new { message = "Route not found." });
            return Ok(stops);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Stops)); }
    }

    // POST /api/routes -> create a route (owner = caller). Requires manage_transport.
    [HttpPost]
    public async Task<ActionResult<RouteResponse>> Create(RouteSaveRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await _userAdminService.HasPermissionAsync(userId, SeedData.ManageTransportPermission))
                return Forbid();

            var result = await _transport.CreateRouteAsync(request, userId);
            return MapRouteResult(result);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Create)); }
    }

    // PUT /api/routes/{id} -> rename/recolor a route. Requires manage_transport.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<RouteResponse>> Update(int id, RouteSaveRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await _userAdminService.HasPermissionAsync(userId, SeedData.ManageTransportPermission))
                return Forbid();

            var result = await _transport.UpdateRouteAsync(id, request, userId);
            return MapRouteResult(result);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Update)); }
    }

    // PUT /api/routes/{id}/stops/order -> persist a drag-reorder. Requires manage_transport.
    [HttpPut("{id:int}/stops/order")]
    public async Task<ActionResult<IReadOnlyList<StopResponse>>> ReorderStops(int id, StopOrderRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await _userAdminService.HasPermissionAsync(userId, SeedData.ManageTransportPermission))
                return Forbid();

            var result = await _transport.ReorderStopsAsync(id, request.OrderedStopIds, userId);
            return result.Status switch
            {
                TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
                TransportWriteStatus.InvalidOrder => BadRequest(new
                {
                    message = "The submitted stop order doesn't match this route's stops.",
                    code = "invalid_order",
                }),
                _ => Ok(result.Stops),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(ReorderStops)); }
    }

    private ActionResult<RouteResponse> MapRouteResult(RouteWriteResult result) =>
        result.Status switch
        {
            TransportWriteStatus.RouteNotFound => NotFound(new { message = "Route not found." }),
            TransportWriteStatus.DuplicateName =>
                Conflict(new { message = "A route with that name already exists.", code = "duplicate_name" }),
            _ => Ok(result.Response),
        };
}
