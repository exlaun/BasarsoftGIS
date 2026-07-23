using Basarsoft.Api.DTOs;
using Basarsoft.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Basarsoft.Api.Services;

public sealed class SignalRRouteSimulationPublisher : IRouteSimulationPublisher
{
    private readonly IHubContext<TransportationHub> _hub;

    public SignalRRouteSimulationPublisher(IHubContext<TransportationHub> hub)
    {
        _hub = hub;
    }

    public Task PublishAsync(RouteSimulationResponse state, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(TransportationHub.RouteGroup(state.RouteId))
            .SendAsync(TransportationHub.VehiclePositionUpdated, state, cancellationToken);
}
