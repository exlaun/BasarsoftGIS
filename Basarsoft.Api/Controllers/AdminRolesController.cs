using Basarsoft.Api.DTOs;
using Basarsoft.Api.Security;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// Admin role management + the role's permission assignment.
[ApiController]
[Authorize(Policy = AdminAccessRequirement.PolicyName)]
[Route("api/admin/roles")]
public class AdminRolesController : ControllerBase
{
    private readonly IRoleService _roles;
    private readonly ILogger<AdminRolesController> _logger;

    public AdminRolesController(IRoleService roles, ILogger<AdminRolesController> logger)
    {
        _roles = roles;
        _logger = logger;
    }

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in AdminRolesController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleResponse>>> List()
    {
        try { return Ok(await _roles.ListAsync()); }
        catch (Exception ex) { return ServerError(ex, nameof(List)); }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoleResponse>> Get(int id)
    {
        try
        {
            var role = await _roles.GetAsync(id);
            return role is null ? NotFound(new { message = "Role not found." }) : Ok(role);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Get)); }
    }

    [HttpPost]
    public async Task<ActionResult<RoleResponse>> Create(RoleCreateRequest request)
    {
        try
        {
            var role = await _roles.CreateAsync(request);
            return role is null
                ? Conflict(new { message = "A role with that name already exists." })
                : Ok(role);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Create)); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoleResponse>> Update(int id, RoleUpdateRequest request)
    {
        try
        {
            var (status, role) = await _roles.UpdateAsync(id, request);
            return status switch
            {
                AdminWriteStatus.NotFound => NotFound(new { message = "Role not found." }),
                AdminWriteStatus.Conflict => Conflict(new { message = "A role with that name already exists." }),
                _ => Ok(role),
            };
        }
        catch (Exception ex) { return ServerError(ex, nameof(Update)); }
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            return await _roles.DeleteAsync(id)
                ? NoContent()
                : NotFound(new { message = "Role not found." });
        }
        catch (Exception ex) { return ServerError(ex, nameof(Delete)); }
    }

    // The ids of the permissions this role grants.
    [HttpGet("{id:int}/permissions")]
    public async Task<ActionResult<IReadOnlyList<int>>> GetPermissions(int id)
    {
        try
        {
            var role = await _roles.GetAsync(id);
            return role is null ? NotFound(new { message = "Role not found." }) : Ok(role.PermissionIds);
        }
        catch (Exception ex) { return ServerError(ex, nameof(GetPermissions)); }
    }

    // Replace the role's permission set with request.Ids.
    [HttpPut("{id:int}/permissions")]
    public async Task<ActionResult> SetPermissions(int id, IdListRequest request)
    {
        try
        {
            return await _roles.SetPermissionsAsync(id, request.Ids)
                ? NoContent()
                : NotFound(new { message = "Role not found." });
        }
        catch (Exception ex) { return ServerError(ex, nameof(SetPermissions)); }
    }
}
