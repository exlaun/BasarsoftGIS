using Microsoft.AspNetCore.Authorization;

namespace Basarsoft.Api.Security;

// Marker requirement for the "AdminAccess" policy: the caller must hold a management permission.
// The actual DB check lives in AdminAccessHandler.
public class AdminAccessRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "AdminAccess";
}
