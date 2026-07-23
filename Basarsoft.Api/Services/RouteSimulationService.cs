using Basarsoft.Api.DTOs;
using Basarsoft.Api.Settings;
using Microsoft.Extensions.Options;

namespace Basarsoft.Api.Services;

public sealed class RouteSimulationService : IRouteSimulationService
{
    private readonly IRouteSimulationStateStore _store;
    private readonly IRouteSimulationRouteLoader _routeLoader;
    private readonly IRouteSimulationPublisher _publisher;
    private readonly RouteSimulationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RouteSimulationService> _logger;

    public RouteSimulationService(
        IRouteSimulationStateStore store,
        IRouteSimulationRouteLoader routeLoader,
        IRouteSimulationPublisher publisher,
        IOptions<RouteSimulationSettings> settings,
        TimeProvider timeProvider,
        ILogger<RouteSimulationService> logger)
    {
        _store = store;
        _routeLoader = routeLoader;
        _publisher = publisher;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RouteSimulationOperationResult> GetAsync(
        int routeId,
        CancellationToken cancellationToken = default)
    {
        if (!await _routeLoader.ExistsAsync(routeId, cancellationToken))
            return new(RouteSimulationOperationStatus.RouteNotFound);

        if (_store.TryGet(routeId, out var run))
        {
            lock (run!.SyncRoot)
                return new(RouteSimulationOperationStatus.Success,
                    InMemoryRouteSimulationStateStore.Clone(run.State));
        }

        return new(RouteSimulationOperationStatus.Success, NotStarted(routeId));
    }

    public async Task<RouteSimulationOperationResult> StartAsync(
        int routeId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        // Fast conflict path before the DB work. TryStart below remains the authoritative race guard.
        if (_store.IsRunning(routeId) && _store.TryGet(routeId, out var activeRun))
        {
            lock (activeRun!.SyncRoot)
                return new(RouteSimulationOperationStatus.SimulationAlreadyRunning,
                    InMemoryRouteSimulationStateStore.Clone(activeRun.State));
        }

        var loaded = await _routeLoader.LoadForStartAsync(routeId, userId, cancellationToken);
        if (loaded.Status != RouteSimulationOperationStatus.Success)
            return new(loaded.Status);
        if (!RouteSimulationPath.TryCreate(loaded.Snapshot!, out var path))
            return new(RouteSimulationOperationStatus.InvalidGeometry);

        var now = _timeProvider.GetUtcNow();
        var initial = path!.PositionAtProgress(0);
        var state = new RouteSimulationResponse
        {
            RunId = Guid.NewGuid(),
            RouteId = routeId,
            Status = RouteSimulationStatus.Running,
            Longitude = initial.Longitude,
            Latitude = initial.Latitude,
            ProgressPercent = 0,
            CurrentStopIndex = initial.CurrentStopIndex,
            CurrentStopName = initial.CurrentStopName,
            StartedAt = now,
            UpdatedAt = now,
            Sequence = 1,
        };
        var run = new RouteSimulationRun
        {
            RunId = state.RunId.Value,
            RouteId = routeId,
            Path = path,
            StartedAt = now,
            WallClockDuration = SimulationDuration(loaded.Snapshot!, path.TotalDistanceMeters),
            State = state,
        };

        if (!_store.TryStart(run, out var current))
            return new(RouteSimulationOperationStatus.SimulationAlreadyRunning, current);

        var response = InMemoryRouteSimulationStateStore.Clone(state);
        await PublishSafelyAsync(response, cancellationToken);
        return new(RouteSimulationOperationStatus.Success, response);
    }

    public async Task<RouteSimulationOperationResult> StopAsync(
        int routeId,
        CancellationToken cancellationToken = default)
    {
        if (!await _routeLoader.ExistsAsync(routeId, cancellationToken))
            return new(RouteSimulationOperationStatus.RouteNotFound);
        if (!_store.TryGet(routeId, out var run))
            return new(RouteSimulationOperationStatus.SimulationNotRunning, NotStarted(routeId));

        RouteSimulationResponse stopped;
        lock (run!.SyncRoot)
        {
            if (run.State.Status != RouteSimulationStatus.Running)
                return new(RouteSimulationOperationStatus.SimulationNotRunning,
                    InMemoryRouteSimulationStateStore.Clone(run.State));

            var now = _timeProvider.GetUtcNow();
            run.State.Status = RouteSimulationStatus.Stopped;
            run.State.StoppedAt = now;
            run.State.UpdatedAt = now;
            run.State.Sequence++;
            run.ElapsedBeforeStart = TimeSpan.FromTicks((long)Math.Round(
                run.WallClockDuration.Ticks * Math.Clamp(run.State.ProgressPercent / 100d, 0, 1)));
            stopped = InMemoryRouteSimulationStateStore.Clone(run.State);
        }

        await PublishSafelyAsync(stopped, cancellationToken);
        return new(RouteSimulationOperationStatus.Success, stopped);
    }

    public async Task<RouteSimulationOperationResult> ResumeAsync(
        int routeId,
        CancellationToken cancellationToken = default)
    {
        if (!await _routeLoader.ExistsAsync(routeId, cancellationToken))
            return new(RouteSimulationOperationStatus.RouteNotFound);
        if (!_store.TryGet(routeId, out var run))
            return new(RouteSimulationOperationStatus.SimulationNotRunning, NotStarted(routeId));

        RouteSimulationResponse resumed;
        lock (run!.SyncRoot)
        {
            if (run.State.Status == RouteSimulationStatus.Running)
                return new(RouteSimulationOperationStatus.SimulationAlreadyRunning,
                    InMemoryRouteSimulationStateStore.Clone(run.State));
            if (run.State.Status != RouteSimulationStatus.Stopped)
                return new(RouteSimulationOperationStatus.SimulationNotRunning,
                    InMemoryRouteSimulationStateStore.Clone(run.State));

            var now = _timeProvider.GetUtcNow();
            run.StartedAt = now;
            run.State.Status = RouteSimulationStatus.Running;
            run.State.StoppedAt = null;
            run.State.UpdatedAt = now;
            run.State.Sequence++;
            resumed = InMemoryRouteSimulationStateStore.Clone(run.State);
        }

        await PublishSafelyAsync(resumed, cancellationToken);
        return new(RouteSimulationOperationStatus.Success, resumed);
    }

    public async Task<RouteSimulationOperationResult> EndAsync(
        int routeId,
        CancellationToken cancellationToken = default)
    {
        // Remove any run (running or terminal) and announce NotStarted. Followers drop their marker and
        // the controls fall back to Start, exactly like a route that was never simulated. This reset is
        // deliberately independent of the database: ending an in-memory run must remain no-op safe even
        // if the route was removed or the database is briefly unavailable.
        _store.TryRemove(routeId, out _);
        var reset = NotStarted(routeId);
        await PublishSafelyAsync(reset, cancellationToken);
        return new(RouteSimulationOperationStatus.Success, reset);
    }

    // Called by RouteSimulationWorker and directly by deterministic unit tests.
    public async Task AdvanceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        foreach (var run in _store.RunningRuns())
        {
            RouteSimulationResponse? update = null;
            try
            {
                lock (run.SyncRoot)
                {
                    if (!_store.IsCurrent(run) || run.State.Status != RouteSimulationStatus.Running)
                        continue;

                    var elapsed = run.ElapsedBeforeStart + (now - run.StartedAt);
                    var fraction = run.WallClockDuration <= TimeSpan.Zero
                        ? 1
                        : elapsed.TotalMilliseconds / run.WallClockDuration.TotalMilliseconds;
                    fraction = Math.Clamp(fraction, 0, 1);
                    var position = run.Path.PositionAtProgress(fraction);
                    run.State.Longitude = position.Longitude;
                    run.State.Latitude = position.Latitude;
                    run.State.ProgressPercent = fraction >= 1 ? 100 : Math.Clamp(position.ProgressPercent, 0, 100);
                    run.State.CurrentStopIndex = position.CurrentStopIndex;
                    run.State.CurrentStopName = position.CurrentStopName;
                    run.State.UpdatedAt = now;
                    run.State.Sequence++;
                    if (fraction >= 1)
                    {
                        run.State.Status = RouteSimulationStatus.Completed;
                        run.State.CompletedAt = now;
                    }
                    update = InMemoryRouteSimulationStateStore.Clone(run.State);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vehicle simulation failed for route {RouteId}", run.RouteId);
                lock (run.SyncRoot)
                {
                    if (_store.IsCurrent(run) && run.State.Status == RouteSimulationStatus.Running)
                    {
                        run.State.Status = RouteSimulationStatus.Failed;
                        run.State.FailureCode = "simulation_failed";
                        run.State.UpdatedAt = now;
                        run.State.Sequence++;
                        update = InMemoryRouteSimulationStateStore.Clone(run.State);
                    }
                }
            }

            if (update is not null)
                await PublishSafelyAsync(update, cancellationToken);
        }
    }

    private TimeSpan SimulationDuration(RouteSimulationRouteSnapshot snapshot, double geometryDistance)
    {
        double baseSeconds;
        if (snapshot.DurationSeconds is > 0 and var duration && double.IsFinite(duration))
            baseSeconds = duration;
        else if (snapshot.DistanceMeters is > 0 and var distance && double.IsFinite(distance))
            baseSeconds = distance / _settings.FallbackSpeedMetersPerSecond;
        else
            baseSeconds = geometryDistance / _settings.FallbackSpeedMetersPerSecond;

        var scaledSeconds = baseSeconds / _settings.TimeScale;
        var minimumSeconds = _settings.UpdateIntervalMilliseconds / 1000d;
        return TimeSpan.FromSeconds(Math.Max(minimumSeconds, scaledSeconds));
    }

    private RouteSimulationResponse NotStarted(int routeId) => new()
    {
        RouteId = routeId,
        Status = RouteSimulationStatus.NotStarted,
        UpdatedAt = _timeProvider.GetUtcNow(),
    };

    private async Task PublishSafelyAsync(
        RouteSimulationResponse state,
        CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.PublishAsync(state, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Delivery loss must not stop the authoritative simulation. A reconnected follower joins
            // the group again and receives the current snapshot directly from the hub.
            _logger.LogError(ex, "Could not publish vehicle state for route {RouteId}", state.RouteId);
        }
    }
}
