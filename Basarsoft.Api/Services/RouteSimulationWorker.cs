using Basarsoft.Api.Settings;
using Microsoft.Extensions.Options;

namespace Basarsoft.Api.Services;

public sealed class RouteSimulationWorker : BackgroundService
{
    private readonly RouteSimulationService _simulations;
    private readonly TimeProvider _timeProvider;
    private readonly RouteSimulationSettings _settings;

    public RouteSimulationWorker(
        RouteSimulationService simulations,
        TimeProvider timeProvider,
        IOptions<RouteSimulationSettings> settings)
    {
        _simulations = simulations;
        _timeProvider = timeProvider;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(_settings.UpdateIntervalMilliseconds),
            _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await _simulations.AdvanceAsync(_timeProvider.GetUtcNow(), stoppingToken);
    }
}
