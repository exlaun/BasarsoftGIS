using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Basarsoft.Api.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Basarsoft.Api.Tests;

public class RouteSimulationServiceTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    private static RouteSimulationRouteSnapshot ValidSnapshot() => new(
        7,
        [(29, 40), (29, 41), (30, 41)],
        [new("First", 29, 40), new("Last", 30, 41)],
        DistanceMeters: 200_000,
        DurationSeconds: 60);

    private static (RouteSimulationService Service, RecordingPublisher Publisher, ManualTimeProvider Clock)
        CreateService(
            RouteSimulationSnapshotResult? loadResult = null,
            InMemoryRouteSimulationStateStore? store = null)
    {
        var publisher = new RecordingPublisher();
        var clock = new ManualTimeProvider(StartTime);
        var service = new RouteSimulationService(
            store ?? new InMemoryRouteSimulationStateStore(),
            new RouteLoaderStub(loadResult ?? new(
                RouteSimulationOperationStatus.Success, ValidSnapshot())),
            publisher,
            Options.Create(new RouteSimulationSettings
            {
                UpdateIntervalMilliseconds = 1000,
                TimeScale = 60,
                FallbackSpeedMetersPerSecond = 13.89,
            }),
            clock,
            NullLogger<RouteSimulationService>.Instance);
        return (service, publisher, clock);
    }

    [Fact]
    public async Task Start_BeginsAtFirstStop_AndRejectsDuplicateRun()
    {
        var (service, publisher, _) = CreateService();

        var started = await service.StartAsync(7, 1);
        var duplicate = await service.StartAsync(7, 1);

        Assert.Equal(RouteSimulationOperationStatus.Success, started.Status);
        Assert.Equal(RouteSimulationStatus.Running, started.State!.Status);
        Assert.Equal(29, started.State.Longitude);
        Assert.Equal(40, started.State.Latitude);
        Assert.Equal(0, started.State.ProgressPercent);
        Assert.Equal(0, started.State.CurrentStopIndex);
        Assert.Equal("First", started.State.CurrentStopName);
        Assert.Equal(RouteSimulationOperationStatus.SimulationAlreadyRunning, duplicate.Status);
        Assert.Single(publisher.States);
    }

    [Fact]
    public async Task Advance_FollowsPolyline_AndCompletesExactlyAtFinalStop()
    {
        var (service, publisher, _) = CreateService();
        var started = await service.StartAsync(7, 1);

        await service.AdvanceAsync(StartTime.AddMilliseconds(500));
        var halfway = publisher.States[^1];
        Assert.Equal(RouteSimulationStatus.Running, halfway.Status);
        Assert.InRange(halfway.ProgressPercent, 49.99, 50.01);
        // The first half follows the northbound polyline leg, not the diagonal between stops.
        Assert.InRange(halfway.Longitude!.Value, 28.9999, 29.0001);
        Assert.InRange(halfway.Latitude!.Value, 40.8, 40.9);

        await service.AdvanceAsync(StartTime.AddSeconds(1));
        var completed = publisher.States[^1];
        Assert.Equal(RouteSimulationStatus.Completed, completed.Status);
        Assert.Equal(100, completed.ProgressPercent);
        Assert.Equal(30, completed.Longitude);
        Assert.Equal(41, completed.Latitude);
        Assert.Equal(1, completed.CurrentStopIndex);
        Assert.Equal("Last", completed.CurrentStopName);
        Assert.NotNull(completed.CompletedAt);
        Assert.True(completed.Sequence > started.State!.Sequence);
    }

    [Fact]
    public async Task StopRetainsPosition_AndCompletedOrStoppedRunCanRestart()
    {
        var (service, publisher, _) = CreateService();
        var first = await service.StartAsync(7, 1);
        await service.AdvanceAsync(StartTime.AddMilliseconds(400));
        var stopped = await service.StopAsync(7);

        Assert.Equal(RouteSimulationStatus.Stopped, stopped.State!.Status);
        Assert.Equal(publisher.States[^2].Longitude, stopped.State.Longitude);
        Assert.NotNull(stopped.State.StoppedAt);

        var restarted = await service.StartAsync(7, 1);
        Assert.Equal(RouteSimulationOperationStatus.Success, restarted.Status);
        Assert.NotEqual(first.State!.RunId, restarted.State!.RunId);
        Assert.Equal(0, restarted.State.ProgressPercent);

        await service.AdvanceAsync(StartTime.AddSeconds(1));
        Assert.Equal(RouteSimulationStatus.Completed, publisher.States[^1].Status);
        var afterCompletion = await service.StartAsync(7, 1);
        Assert.Equal(RouteSimulationOperationStatus.Success, afterCompletion.Status);
        Assert.NotEqual(restarted.State.RunId, afterCompletion.State!.RunId);
    }

    [Fact]
    public async Task Resume_ContinuesFromStoppedProgress_InsteadOfStartingOver()
    {
        var (service, publisher, clock) = CreateService();
        await service.StartAsync(7, 1);
        await service.AdvanceAsync(StartTime.AddMilliseconds(400));
        clock.SetUtcNow(StartTime.AddMilliseconds(400));
        var stopped = await service.StopAsync(7);

        clock.SetUtcNow(StartTime.AddMilliseconds(600));
        var resumed = await service.ResumeAsync(7);
        await service.AdvanceAsync(StartTime.AddMilliseconds(900));

        Assert.Equal(RouteSimulationOperationStatus.Success, resumed.Status);
        Assert.Equal(RouteSimulationStatus.Running, resumed.State!.Status);
        Assert.Null(resumed.State.StoppedAt);
        Assert.Equal(stopped.State!.RunId, resumed.State.RunId);
        Assert.InRange(publisher.States[^1].ProgressPercent, 69.99, 70.01);
    }

    [Theory]
    [InlineData(RouteSimulationOperationStatus.RouteNotFound)]
    [InlineData(RouteSimulationOperationStatus.OutsideAuthorizedArea)]
    [InlineData(RouteSimulationOperationStatus.InsufficientStops)]
    [InlineData(RouteSimulationOperationStatus.GeometryMissing)]
    [InlineData(RouteSimulationOperationStatus.GeometryStale)]
    [InlineData(RouteSimulationOperationStatus.InvalidGeometry)]
    public async Task Start_ReturnsRouteValidationFailure(RouteSimulationOperationStatus status)
    {
        var (service, publisher, _) = CreateService(new(status));

        var result = await service.StartAsync(7, 1);

        Assert.Equal(status, result.Status);
        Assert.Empty(publisher.States);
    }

    [Fact]
    public async Task FreshStoreAfterRestartReportsNotStarted()
    {
        var (service, _, _) = CreateService(store: new InMemoryRouteSimulationStateStore());

        var state = await service.GetAsync(7);

        Assert.Equal(RouteSimulationStatus.NotStarted, state.State!.Status);
        Assert.Null(state.State.RunId);
    }

    [Fact]
    public async Task End_ClearsRunningRunToNotStarted_AndReportsItAfterwards()
    {
        var (service, publisher, _) = CreateService();
        await service.StartAsync(7, 1);

        var ended = await service.EndAsync(7);

        Assert.Equal(RouteSimulationOperationStatus.Success, ended.Status);
        Assert.Equal(RouteSimulationStatus.NotStarted, ended.State!.Status);
        Assert.Null(ended.State.RunId);
        // The reset is broadcast so followers drop their marker, and a later GET stays NotStarted.
        Assert.Equal(RouteSimulationStatus.NotStarted, publisher.States[^1].Status);
        var after = await service.GetAsync(7);
        Assert.Equal(RouteSimulationStatus.NotStarted, after.State!.Status);
    }

    [Fact]
    public async Task End_IsIdempotent_EvenWhenTheRouteCannotBeLoaded()
    {
        var (service, _, _) = CreateService();
        await service.StartAsync(7, 1);
        await service.StopAsync(7);

        var stoppedRun = await service.EndAsync(7);
        var repeated = await service.EndAsync(7);
        Assert.Equal(RouteSimulationOperationStatus.Success, stoppedRun.Status);
        Assert.Equal(RouteSimulationStatus.NotStarted, stoppedRun.State!.Status);
        Assert.Equal(RouteSimulationOperationStatus.Success, repeated.Status);
        Assert.Equal(RouteSimulationStatus.NotStarted, repeated.State!.Status);

        var (missing, _, _) = CreateService(new(RouteSimulationOperationStatus.RouteNotFound));
        var missingResult = await missing.EndAsync(7);
        Assert.Equal(RouteSimulationOperationStatus.Success, missingResult.Status);
        Assert.Equal(RouteSimulationStatus.NotStarted, missingResult.State!.Status);
    }

    [Fact]
    public void PathRejectsZeroLengthGeometry_AndClampsEndpoints()
    {
        var invalid = ValidSnapshot() with { GeometryCoordinates = [(29, 40), (29, 40)] };
        Assert.False(RouteSimulationPath.TryCreate(invalid, out _));

        Assert.True(RouteSimulationPath.TryCreate(ValidSnapshot(), out var path));
        Assert.Equal(0, path!.PositionAtProgress(-1).ProgressPercent);
        var final = path.PositionAtProgress(2);
        Assert.Equal(100, final.ProgressPercent);
        Assert.Equal((30d, 41d), (final.Longitude, final.Latitude));
    }

    [Fact]
    public void NearestStop_UsesDistanceAlongRoute_NotStraightLineDistance()
    {
        var loop = new RouteSimulationRouteSnapshot(
            8,
            [(0, 0), (0, 0.01), (0.01, 0.01), (0.01, 0), (0, 0.001)],
            [
                new("First", 0, 0),
                new("Middle", 0.01, 0.01),
                new("Last", 0, 0.001),
            ],
            DistanceMeters: null,
            DurationSeconds: null);

        Assert.True(RouteSimulationPath.TryCreate(loop, out var path));
        // Early in the trip the vehicle passes exactly through the future final stop. Straight-line
        // proximity would call it "Last"; route-distance proximity correctly keeps "First".
        var early = path!.PositionAtProgress(0.025);
        Assert.Equal(0, early.CurrentStopIndex);
        Assert.Equal("First", early.CurrentStopName);

        var late = path.PositionAtProgress(0.98);
        Assert.Equal(2, late.CurrentStopIndex);
        Assert.Equal("Last", late.CurrentStopName);
    }

    private sealed class RouteLoaderStub : IRouteSimulationRouteLoader
    {
        private readonly RouteSimulationSnapshotResult _result;
        public RouteLoaderStub(RouteSimulationSnapshotResult result) => _result = result;
        public Task<bool> ExistsAsync(int routeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result.Status != RouteSimulationOperationStatus.RouteNotFound);
        public Task<RouteSimulationSnapshotResult> LoadForStartAsync(
            int routeId,
            int userId,
            CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }

    private sealed class RecordingPublisher : IRouteSimulationPublisher
    {
        public List<RouteSimulationResponse> States { get; } = [];
        public Task PublishAsync(RouteSimulationResponse state, CancellationToken cancellationToken = default)
        {
            States.Add(state);
            return Task.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset now) => _now = now;
    }
}
