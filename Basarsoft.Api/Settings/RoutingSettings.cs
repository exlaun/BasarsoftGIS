namespace Basarsoft.Api.Settings;

// OSRM Route API endpoints. The local Docker service is primary; a direct fallback is opt-in so a
// deployment never sends route coordinates to a third party without an explicit configuration.
public class RoutingSettings
{
    public string PrimaryBaseUrl { get; set; } = "http://localhost:5001";

    public string? FallbackBaseUrl { get; set; }

    public string Profile { get; set; } = "driving";

    public int TimeoutSeconds { get; set; } = 10;
}
