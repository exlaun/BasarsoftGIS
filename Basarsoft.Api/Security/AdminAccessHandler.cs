using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace Basarsoft.Api.Security;

// Succeeds the AdminAccess policy when the JWT's user holds a management permission (checked against the
// DB via IUserAdminService). Registered as scoped so it can consume the scoped service / DbContext.
public class AdminAccessHandler : AuthorizationHandler<AdminAccessRequirement>
{
    private readonly IUserAdminService _userAdminService;

    public AdminAccessHandler(IUserAdminService userAdminService)
    {
        _userAdminService = userAdminService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminAccessRequirement requirement)
    {
        // MapInboundClaims is off, so the id lives in the raw "sub" claim.
        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (int.TryParse(sub, out var userId) && await _userAdminService.IsAdminAsync(userId))
            context.Succeed(requirement);
    }
}
