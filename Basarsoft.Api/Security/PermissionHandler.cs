using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace Basarsoft.Api.Security;

// Succeeds a per-permission policy when the JWT's user effectively holds that exact permission.
// Same shape as AdminAccessHandler: scoped, DB-backed via IUserAdminService, id from the raw "sub".
public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserAdminService _userAdminService;

    public PermissionHandler(IUserAdminService userAdminService)
    {
        _userAdminService = userAdminService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (int.TryParse(sub, out var userId) &&
            await _userAdminService.HasPermissionAsync(userId, requirement.PermissionName))
        {
            context.Succeed(requirement);
        }
    }
}
