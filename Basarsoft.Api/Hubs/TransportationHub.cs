using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Basarsoft.Api.Hubs;

[Authorize]
public class TransportationHub : Hub
{
    public const string VehiclePositionUpdated = "VehiclePositionUpdated";
    private readonly IRouteSimulationService _simulations;

    public TransportationHub(IRouteSimulationService simulations)
    {
        _simulations = simulations;
    }

    public static string RouteGroup(int routeId) => $"route-{routeId}";

    public async Task JoinRoute(int routeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, RouteGroup(routeId));
        var current = await _simulations.GetAsync(routeId, Context.ConnectionAborted);
        if (current.Status == RouteSimulationOperationStatus.RouteNotFound)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RouteGroup(routeId));
            throw new HubException("route_not_found");
        }
        await Clients.Caller.SendAsync(VehiclePositionUpdated, current.State, Context.ConnectionAborted);
    }

    public Task LeaveRoute(int routeId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RouteGroup(routeId));
}
