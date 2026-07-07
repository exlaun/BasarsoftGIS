namespace Basarsoft.Api.DTOs;

// Generic body for the "set assignments" endpoints (a user's roles, a role's permissions, a user's
// direct permissions). Ids is the COMPLETE desired set — the service diffs it against what's stored.
public class IdListRequest
{
    public IReadOnlyList<int> Ids { get; set; } = Array.Empty<int>();
}
