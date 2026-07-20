namespace Basarsoft.Api.Settings;

// Strongly-typed view of the "GeoServer" section in appsettings.json. Bound once in Program.cs and
// injected into GeoServerReadService, which now reads the drawn geometry back through GeoServer's WFS
// (instead of straight from EF Core) for the map's one-shot load.
public class GeoServerSettings
{
    // Root of the local GeoServer, e.g. http://localhost:8080/geoserver.
    public string BaseUrl { get; set; } = string.Empty;

    // The workspace the point/line/polygon layers are published under (e.g. "basarsoft").
    public string Workspace { get; set; } = string.Empty;

    // Optional Basic-Auth account the API uses toward GeoServer. Empty = anonymous (local-dev
    // default). Once GeoServer's workspace denies anonymous read (see geoserver/README.md), these
    // MUST be set (user-secrets / GeoServer__Username / GeoServer__Password) — the per-user data
    // isolation of the vw_* views is only real when GeoServer answers nobody but this API.
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
