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
}
