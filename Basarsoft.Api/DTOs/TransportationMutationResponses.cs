namespace Basarsoft.Api.DTOs;

// Stop creation returns both committed records because adding the stop may also rebuild its route.
public class StopCreateResponse
{
    public StopResponse Stop { get; set; } = default!;

    public RouteResponse Route { get; set; } = default!;
}

// Reorder returns the authoritative contiguous order and the route geometry produced from it.
public class RouteStopsResponse
{
    public IReadOnlyList<StopResponse> Stops { get; set; } = Array.Empty<StopResponse>();

    public RouteResponse Route { get; set; } = default!;
}

// One policy-protected, grouped snapshot keeps the admin page from making an N+1 request per route.
public class AdminTransportationResponse
{
    public IReadOnlyList<AdminTransportationRouteResponse> Routes { get; set; } =
        Array.Empty<AdminTransportationRouteResponse>();
}

public class AdminTransportationRouteResponse
{
    public RouteResponse Route { get; set; } = default!;

    public IReadOnlyList<StopResponse> Stops { get; set; } = Array.Empty<StopResponse>();
}
