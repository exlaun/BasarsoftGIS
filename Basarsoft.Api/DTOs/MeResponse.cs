namespace Basarsoft.Api.DTOs;

// Returned by GET /api/auth/me — identity plus the caller's RBAC context. IsAdmin tells the client
// whether to show the admin-panel button and allow /admin; Permissions also gates map draw tools. This
// is a read-only DB lookup and does NOT change the JWT.
public class MeResponse
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();

    // The caller's effective geographic authorization area as WKT (own area, else union of role
    // areas — possibly a MULTIPOLYGON). Null = unrestricted drawing. The map uses this to show the
    // boundary and pre-check draws; the server re-checks on every save regardless.
    public string? AuthorizedAreaWkt { get; set; }
}
