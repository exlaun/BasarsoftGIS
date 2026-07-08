using Basarsoft.Api.DTOs;
using Basarsoft.Api.Security;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// Admin user management. The AdminAccess policy gates every action (caller must hold a management
// permission). Each action wraps its work in try/catch → ServerError, mirroring the other controllers.
[ApiController]
[Authorize(Policy = AdminAccessRequirement.PolicyName)]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserAdminService _users;
    private readonly IGeoAuthorizationService _geoAuth;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IUserAdminService users,
        IGeoAuthorizationService geoAuth,
        ILogger<AdminUsersController> logger)
    {
        _users = users;
        _geoAuth = geoAuth;
        _logger = logger;
    }

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in AdminUsersController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminUserResponse>>> List()
    {
        try { return Ok(await _users.ListAsync()); }
        catch (Exception ex) { return ServerError(ex, nameof(List)); }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdminUserResponse>> Get(int id)
    {
        try
        {
            var user = await _users.GetAsync(id);
            return user is null ? NotFound(new { message = "User not found." }) : Ok(user);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Get)); }
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserResponse>> Create(AdminUserCreateRequest request)
    {
        try
        {
            var (status, user) = await _users.CreateAsync(request);
            return status == AdminWriteStatus.Conflict
                ? Conflict(new { message = "Username is already taken." })
                : Ok(user);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Create)); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminUserResponse>> Update(int id, AdminUserUpdateRequest request)
    {
        try
        {
            var (status, user) = await _users.UpdateAsync(id, request);
            return status switch
            {
                AdminWriteStatus.NotFound => NotFound(new { message = "User not found." }),
                AdminWriteStatus.Conflict => Conflict(new { message = "Username is already taken." }),
                _ => Ok(user),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(Update)); }
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            return await _users.DeleteAsync(id)
                ? NoContent()
                : NotFound(new { message = "User not found." });
        }
        catch (Exception ex) { return ServerError(ex, nameof(Delete)); }
    }

    // The roles the user holds (for the role-assignment editor).
    [HttpGet("{id:int}/roles")]
    public async Task<ActionResult<IReadOnlyList<RoleSummary>>> GetRoles(int id)
    {
        try
        {
            var user = await _users.GetAsync(id);
            return user is null ? NotFound(new { message = "User not found." }) : Ok(user.Roles);
        }
        catch (Exception ex) { return ServerError(ex, nameof(GetRoles)); }
    }

    // Replace the user's role set with request.Ids.
    [HttpPut("{id:int}/roles")]
    public async Task<ActionResult> SetRoles(int id, IdListRequest request)
    {
        try
        {
            return await _users.SetRolesAsync(id, request.Ids)
                ? NoContent()
                : NotFound(new { message = "User not found." });
        }
        catch (Exception ex) { return ServerError(ex, nameof(SetRoles)); }
    }

    // Effective permissions (inherited-from-role vs direct vs none) — drives the inheritance-aware editor.
    [HttpGet("{id:int}/permissions")]
    public async Task<ActionResult<IReadOnlyList<EffectivePermissionResponse>>> GetPermissions(int id)
    {
        try
        {
            var perms = await _users.GetEffectivePermissionsAsync(id);
            return perms is null ? NotFound(new { message = "User not found." }) : Ok(perms);
        }
        catch (Exception ex) { return ServerError(ex, nameof(GetPermissions)); }
    }

    // Replace the user's DIRECT permission grants with request.Ids (role-derived ones are untouched).
    [HttpPut("{id:int}/permissions")]
    public async Task<ActionResult> SetPermissions(int id, IdListRequest request)
    {
        try
        {
            return await _users.SetDirectPermissionsAsync(id, request.Ids)
                ? NoContent()
                : NotFound(new { message = "User not found." });
        }
        catch (Exception ex) { return ServerError(ex, nameof(SetPermissions)); }
    }

    // The user's geographic authorization area as WKT (wkt null = none assigned).
    [HttpGet("{id:int}/geo-area")]
    public async Task<ActionResult<GeoAreaResponse>> GetGeoArea(int id)
    {
        try
        {
            var area = await _geoAuth.GetForUserAsync(id);
            return area is null ? NotFound(new { message = "User not found." }) : Ok(area);
        }
        catch (Exception ex) { return ServerError(ex, nameof(GetGeoArea)); }
    }

    // Assign/replace the user's geographic authorization area (polygon WKT, EPSG:4326).
    [HttpPut("{id:int}/geo-area")]
    public async Task<ActionResult> SetGeoArea(int id, GeoAreaRequest request)
    {
        try
        {
            return await _geoAuth.SetForUserAsync(id, request.Wkt) switch
            {
                GeoAreaWriteStatus.NotFound => NotFound(new { message = "User not found." }),
                GeoAreaWriteStatus.InvalidGeometry =>
                    BadRequest(new { message = "WKT must be a single valid polygon." }),
                _ => NoContent(),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(SetGeoArea)); }
    }

    // Remove the user's geographic authorization area (drawing becomes unrestricted for them,
    // unless one of their roles still has an area).
    [HttpDelete("{id:int}/geo-area")]
    public async Task<ActionResult> ClearGeoArea(int id)
    {
        try
        {
            return await _geoAuth.ClearForUserAsync(id)
                ? NoContent()
                : NotFound(new { message = "User has no assigned area." });
        }
        catch (Exception ex) { return ServerError(ex, nameof(ClearGeoArea)); }
    }
}
