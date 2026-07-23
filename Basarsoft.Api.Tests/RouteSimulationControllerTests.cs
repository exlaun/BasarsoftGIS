using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Basarsoft.Api.Controllers;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Hubs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Basarsoft.Api.Tests;

public class RouteSimulationControllerTests
{
    [Fact]
    public void EndActionKeepsItsPublicPostRouteContract()
    {
        var controllerRoute = Assert.Single(
            typeof(RouteSimulationsController).GetCustomAttributes<RouteAttribute>());
        var endMethod = typeof(RouteSimulationsController).GetMethod(
            nameof(RouteSimulationsController.End));
        var endRoute = Assert.Single(endMethod!.GetCustomAttributes<HttpPostAttribute>());

        Assert.Equal("api/routes/{routeId:int}/simulation", controllerRoute.Template);
        Assert.Equal("end", endRoute.Template);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OperatorOrAdminCanStart(bool operatorPermission, bool isAdmin)
    {
        var service = new SimulationStub();
        var controller = WithUser(new RouteSimulationsController(
            service,
            new UserStub { Operator = operatorPermission, Admin = isAdmin },
            NullLogger<RouteSimulationsController>.Instance));

        var result = await controller.Start(7, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, service.StartCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OperatorOrAdminCanResumeAndEnd(bool operatorPermission, bool isAdmin)
    {
        var service = new SimulationStub();
        var controller = WithUser(new RouteSimulationsController(
            service,
            new UserStub { Operator = operatorPermission, Admin = isAdmin },
            NullLogger<RouteSimulationsController>.Instance));

        var resume = await controller.Resume(7, CancellationToken.None);
        var result = await controller.End(7, CancellationToken.None);

        Assert.IsType<OkObjectResult>(resume.Result);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(RouteSimulationStatus.NotStarted, ((RouteSimulationResponse)ok.Value!).Status);
        Assert.Equal(1, service.ResumeCalls);
        Assert.Equal(1, service.EndCalls);
    }

    [Fact]
    public async Task ViewerCannotStartStopResumeOrEnd()
    {
        var service = new SimulationStub();
        var controller = WithUser(new RouteSimulationsController(
            service, new UserStub(), NullLogger<RouteSimulationsController>.Instance));

        Assert.IsType<ForbidResult>((await controller.Start(7, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await controller.Stop(7, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await controller.Resume(7, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await controller.End(7, CancellationToken.None)).Result);
        Assert.Equal(0, service.StartCalls);
        Assert.Equal(0, service.StopCalls);
        Assert.Equal(0, service.ResumeCalls);
        Assert.Equal(0, service.EndCalls);
    }

    [Theory]
    [InlineData(RouteSimulationOperationStatus.InsufficientStops, 409, "insufficient_stops")]
    [InlineData(RouteSimulationOperationStatus.GeometryMissing, 422, "route_geometry_missing")]
    [InlineData(RouteSimulationOperationStatus.GeometryStale, 422, "stale_route_geometry")]
    [InlineData(RouteSimulationOperationStatus.InvalidGeometry, 422, "invalid_route_geometry")]
    [InlineData(RouteSimulationOperationStatus.SimulationAlreadyRunning, 409, "simulation_already_running")]
    public async Task StartMapsStableErrorCodes(
        RouteSimulationOperationStatus status,
        int httpStatus,
        string code)
    {
        var controller = WithUser(new RouteSimulationsController(
            new SimulationStub { StartResult = new(status) },
            new UserStub { Operator = true },
            NullLogger<RouteSimulationsController>.Instance));

        var result = await controller.Start(7, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(httpStatus, objectResult.StatusCode);
        Assert.Equal(code, Property<string>(objectResult.Value!, "code"));
    }

    [Fact]
    public void SignalRGroupNameIsRouteSpecific()
    {
        Assert.Equal("route-12", TransportationHub.RouteGroup(12));
        Assert.Equal("VehiclePositionUpdated", TransportationHub.VehiclePositionUpdated);
    }

    private static T Property<T>(object value, string name) =>
        (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private static T WithUser<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, "1")], "test")),
            },
        };
        return controller;
    }

    private sealed class SimulationStub : IRouteSimulationService
    {
        private static readonly RouteSimulationResponse Running = new()
        {
            RouteId = 7,
            RunId = Guid.NewGuid(),
            Status = RouteSimulationStatus.Running,
        };
        public RouteSimulationOperationResult StartResult { get; init; } =
            new(RouteSimulationOperationStatus.Success, Running);
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int ResumeCalls { get; private set; }
        public int EndCalls { get; private set; }
        public Task<RouteSimulationOperationResult> GetAsync(int routeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RouteSimulationOperationResult(RouteSimulationOperationStatus.Success, Running));
        public Task<RouteSimulationOperationResult> StartAsync(int routeId, int userId, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return Task.FromResult(StartResult);
        }
        public Task<RouteSimulationOperationResult> StopAsync(int routeId, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return Task.FromResult(new RouteSimulationOperationResult(RouteSimulationOperationStatus.Success, Running));
        }
        public Task<RouteSimulationOperationResult> ResumeAsync(int routeId, CancellationToken cancellationToken = default)
        {
            ResumeCalls++;
            return Task.FromResult(new RouteSimulationOperationResult(RouteSimulationOperationStatus.Success, Running));
        }
        public Task<RouteSimulationOperationResult> EndAsync(int routeId, CancellationToken cancellationToken = default)
        {
            EndCalls++;
            var reset = new RouteSimulationResponse { RouteId = routeId, Status = RouteSimulationStatus.NotStarted };
            return Task.FromResult(new RouteSimulationOperationResult(RouteSimulationOperationStatus.Success, reset));
        }
    }

    private sealed class UserStub : IUserAdminService
    {
        public bool Operator { get; init; }
        public bool Admin { get; init; }
        public Task<bool> HasPermissionAsync(int userId, string permissionName) => Task.FromResult(Operator);
        public Task<bool> IsAdminAsync(int userId) => Task.FromResult(Admin);
        public Task<IReadOnlyList<AdminUserResponse>> ListAsync() => throw new NotSupportedException();
        public Task<AdminUserResponse?> GetAsync(int id) => throw new NotSupportedException();
        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> CreateAsync(AdminUserCreateRequest request) => throw new NotSupportedException();
        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> UpdateAsync(int id, AdminUserUpdateRequest request) => throw new NotSupportedException();
        public Task<AdminWriteStatus> DeleteAsync(int id) => throw new NotSupportedException();
        public Task<bool> SetRolesAsync(int userId, IReadOnlyList<int> roleIds) => throw new NotSupportedException();
        public Task<IReadOnlyList<EffectivePermissionResponse>?> GetEffectivePermissionsAsync(int userId) => throw new NotSupportedException();
        public Task<bool> SetDirectPermissionsAsync(int userId, IReadOnlyList<int> permissionIds) => throw new NotSupportedException();
        public Task<MeResponse?> GetMeAsync(int userId) => throw new NotSupportedException();
    }
}
