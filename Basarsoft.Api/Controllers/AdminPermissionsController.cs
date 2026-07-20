using Basarsoft.Api.DTOs;
using Basarsoft.Api.Security;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// The shared permission catalogue. Mostly seeded; the admin can add or remove entries.
// Gated by manage_permissions specifically.
[ApiController]
[Authorize(Policy = PermissionRequirement.ManagePermissions)]
[Route("api/admin/permissions")]
public class AdminPermissionsController : ControllerBase
{
    private readonly IPermissionService _permissions;
    private readonly ILogger<AdminPermissionsController> _logger;

    public AdminPermissionsController(IPermissionService permissions, ILogger<AdminPermissionsController> logger)
    {
        _permissions = permissions;
        _logger = logger;
    }

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in AdminPermissionsController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PermissionResponse>>> List()
    {
        try { return Ok(await _permissions.ListAsync()); }
        catch (Exception ex) { return ServerError(ex, nameof(List)); }
    }

    [HttpPost]
    public async Task<ActionResult<PermissionResponse>> Create(PermissionCreateRequest request)
    {
        try
        {
            var permission = await _permissions.CreateAsync(request);
            return permission is null
                ? Conflict(new { message = "A permission with that name already exists." })
                : Ok(permission);
        }
        catch (Exception ex) { return ServerError(ex, nameof(Create)); }
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            return await _permissions.DeleteAsync(id)
                ? NoContent()
                : NotFound(new { message = "Permission not found." });
        }
        catch (Exception ex) { return ServerError(ex, nameof(Delete)); }
    }
}
