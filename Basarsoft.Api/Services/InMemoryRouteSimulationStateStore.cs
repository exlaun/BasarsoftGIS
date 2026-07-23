using System.Collections.Concurrent;
using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public sealed class RouteSimulationRun
{
    public required Guid RunId { get; init; }
    public required int RouteId { get; init; }
    public required RouteSimulationPath Path { get; init; }
    public required TimeSpan WallClockDuration { get; init; }
    public required DateTimeOffset StartedAt { get; set; }
    public TimeSpan ElapsedBeforeStart { get; set; }
    public object SyncRoot { get; } = new();
    public RouteSimulationResponse State { get; set; } = default!;
}

public interface IRouteSimulationStateStore : IRouteSimulationStateReader
{
    bool TryStart(RouteSimulationRun run, out RouteSimulationResponse? currentState);
    bool TryGet(int routeId, out RouteSimulationRun? run);
    bool IsCurrent(RouteSimulationRun run);
    bool TryRemove(int routeId, out RouteSimulationRun? removed);
    IReadOnlyList<RouteSimulationRun> RunningRuns();
}

public sealed class InMemoryRouteSimulationStateStore : IRouteSimulationStateStore
{
    private readonly ConcurrentDictionary<int, RouteSimulationRun> _runs = new();
    private readonly object _startGate = new();

    public bool TryStart(RouteSimulationRun run, out RouteSimulationResponse? currentState)
    {
        lock (_startGate)
        {
            if (_runs.TryGetValue(run.RouteId, out var current))
            {
                lock (current.SyncRoot)
                {
                    if (current.State.Status == RouteSimulationStatus.Running)
                    {
                        currentState = Clone(current.State);
                        return false;
                    }
                }
            }

            _runs[run.RouteId] = run;
            currentState = Clone(run.State);
            return true;
        }
    }

    public bool TryGet(int routeId, out RouteSimulationRun? run) => _runs.TryGetValue(routeId, out run);

    // Drop the route's run entirely (used by "End"), so the next GET/join reports NotStarted as if the
    // route had never been simulated. Guarded by the same gate as TryStart so it cannot race a start.
    public bool TryRemove(int routeId, out RouteSimulationRun? removed)
    {
        lock (_startGate)
            return _runs.TryRemove(routeId, out removed);
    }

    public bool IsCurrent(RouteSimulationRun run) =>
        _runs.TryGetValue(run.RouteId, out var current) && ReferenceEquals(current, run);

    public IReadOnlyList<RouteSimulationRun> RunningRuns() => _runs.Values
        .Where(run =>
        {
            lock (run.SyncRoot)
                return run.State.Status == RouteSimulationStatus.Running;
        })
        .ToArray();

    public bool IsRunning(int routeId)
    {
        if (!_runs.TryGetValue(routeId, out var run))
            return false;
        lock (run.SyncRoot)
            return run.State.Status == RouteSimulationStatus.Running;
    }

    public static RouteSimulationResponse Clone(RouteSimulationResponse state) => new()
    {
        RunId = state.RunId,
        RouteId = state.RouteId,
        Status = state.Status,
        Longitude = state.Longitude,
        Latitude = state.Latitude,
        ProgressPercent = state.ProgressPercent,
        CurrentStopIndex = state.CurrentStopIndex,
        CurrentStopName = state.CurrentStopName,
        StartedAt = state.StartedAt,
        CompletedAt = state.CompletedAt,
        StoppedAt = state.StoppedAt,
        UpdatedAt = state.UpdatedAt,
        Sequence = state.Sequence,
        FailureCode = state.FailureCode,
    };
}
