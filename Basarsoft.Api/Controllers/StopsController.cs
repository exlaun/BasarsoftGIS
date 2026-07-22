using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// Transportation stops (points belonging to a route). GET is open to every authenticated user (it
// feeds the map's stop layer); creating a stop requires the manage_transport permission (Operators).
// The module exposes no stop edit/delete and no way to move a stop between routes. Reordering lives in
// RoutesController (PUT /api/routes/{id}/stops/order). Reads stay on EF — nothing here touches GeoServer.
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
    public async Task<ActionResult<StopResponse>> Create(StopCreateRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();
            if (!await _userAdminService.HasPermissionAsync(userId, SeedData.ManageTransportPermission))
                return Forbid();

            var result = await _transport.CreateStopAsync(request, userId);
            return result.Status switch
            {
                TransportWriteStatus.InvalidGeometry =>
                    BadRequest(new { message = "WKT must be a single valid point." }),
                TransportWriteStatus.RouteNotFound =>
                    BadRequest(new { message = "Route not found.", code = "route_not_found" }),
                // The `code` field lets the client tell this 403 apart from the permission 403 above.
                TransportWriteStatus.OutsideAuthorizedArea => StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "The stop is outside your authorized area.", code = "outside_authorized_area" }),
                _ => Ok(result.Response),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(Create)); }
    }
}
