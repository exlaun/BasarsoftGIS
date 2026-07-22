using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Controllers;
using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Basarsoft.Api.Tests;

public class TransportationControllerTests
{
    private static readonly RouteResponse CurrentRoute = new()
    {
        Id = 7,
        Name = "Route",
        GeometryWkt = "LINESTRING (29 41, 30 40)",
        IsGeometryStale = true,
        RoutingErrorCode = "routing_unavailable",
        StopCount = 2,
    };

    [Theory]
    [InlineData(TransportWriteStatus.InsufficientStops, StatusCodes.Status409Conflict)]
    [InlineData(TransportWriteStatus.NoRoute, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(TransportWriteStatus.InvalidCoordinates, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(TransportWriteStatus.RoutingUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task OperationalBuild_MapsRoutingStatusAndCarriesCurrentRoute(
        TransportWriteStatus status,
        int expectedStatus)
    {
        var service = new TransportationStub
        {
            BuildResult = RouteBuildResult.From(status, CurrentRoute),
        };
        var controller = WithUser(new RoutesController(
            service,
            new PermissionStub(),
            NullLogger<RoutesController>.Instance));

        var result = await controller.Build(7, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.Same(CurrentRoute, Property<RouteResponse>(objectResult.Value!, "route"));
    }

    [Fact]
    public async Task OperationalStopFailure_ReturnsPersistedStopAndRoute()
    {
        var stop = new StopResponse { Id = 11, RouteId = 7, SequenceOrder = 2 };
        var service = new TransportationStub
        {
            StopResult = new StopWriteResult(
                TransportWriteStatus.NoRoute, stop, CurrentRoute, StopPersisted: true),
        };
        var controller = WithUser(new StopsController(
            service,
            new PermissionStub(),
            NullLogger<StopsController>.Instance));

        var result = await controller.Create(new StopCreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.True(Property<bool>(objectResult.Value!, "stopPersisted"));
        Assert.Same(stop, Property<StopResponse>(objectResult.Value!, "stop"));
        Assert.Same(CurrentRoute, Property<RouteResponse>(objectResult.Value!, "route"));
    }

    [Fact]
    public async Task OperationalAndAdminReorderFailures_ReturnPersistedOrderState()
    {
        var stops = new[] { new StopResponse { Id = 11 }, new StopResponse { Id = 12 } };
        var service = new TransportationStub
        {
            OrderResult = new StopOrderResult(
                TransportWriteStatus.RoutingUnavailable, stops, CurrentRoute, OrderPersisted: true),
        };
        var operational = WithUser(new RoutesController(
            service,
            new PermissionStub(),
            NullLogger<RoutesController>.Instance));
        var admin = WithUser(new AdminTransportationController(
            service,
            NullLogger<AdminTransportationController>.Instance));
        var request = new StopOrderRequest { OrderedStopIds = [11, 12] };

        var operationalResult = await operational.ReorderStops(7, request, CancellationToken.None);
        var adminResult = await admin.ReorderStops(7, request, CancellationToken.None);

        foreach (var result in new[] { operationalResult.Result, adminResult.Result })
        {
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
            Assert.True(Property<bool>(objectResult.Value!, "orderPersisted"));
            Assert.Same(CurrentRoute, Property<RouteResponse>(objectResult.Value!, "route"));
        }
    }

    [Fact]
    public async Task AdminBuild_UsesSameRoutingFailureContract()
    {
        var service = new TransportationStub
        {
            BuildResult = RouteBuildResult.From(TransportWriteStatus.NoRoute, CurrentRoute),
        };
        var controller = WithUser(new AdminTransportationController(
            service,
            NullLogger<AdminTransportationController>.Instance));

        var result = await controller.BuildRoute(7, CancellationToken.None);

        var objectResult = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Same(CurrentRoute, Property<RouteResponse>(objectResult.Value!, "route"));
    }

    [Fact]
    public async Task Delete_WithoutManageTransport_Is403AndNeverReachesTheService()
    {
        var routeService = new TransportationStub { RouteDeleted = true };
        var routes = WithUser(new RoutesController(
            routeService, new PermissionStub { Granted = false }, NullLogger<RoutesController>.Instance));
        var stopService = new TransportationStub();
        var stops = WithUser(new StopsController(
            stopService, new PermissionStub { Granted = false }, NullLogger<StopsController>.Instance));

        Assert.IsType<ForbidResult>(await routes.Delete(7));
        Assert.IsType<ForbidResult>((await stops.Delete(11, CancellationToken.None)).Result);
        Assert.Equal(0, routeService.DeleteCallCount);
        Assert.Equal(0, stopService.DeleteCallCount);
    }

    [Fact]
    public async Task Delete_MapsMissingRowsTo404AndSuccessToItsPayload()
    {
        var missing = WithUser(new RoutesController(
            new TransportationStub { RouteDeleted = false },
            new PermissionStub(),
            NullLogger<RoutesController>.Instance));
        Assert.IsType<NotFoundObjectResult>(await missing.Delete(999));

        var deleted = WithUser(new RoutesController(
            new TransportationStub { RouteDeleted = true },
            new PermissionStub(),
            NullLogger<RoutesController>.Instance));
        Assert.IsType<NoContentResult>(await deleted.Delete(7));

        var unknownStop = WithUser(new StopsController(
            new TransportationStub { DeleteStopResult = StopOrderResult.StopNotFound },
            new PermissionStub(),
            NullLogger<StopsController>.Instance));
        Assert.IsType<NotFoundObjectResult>((await unknownStop.Delete(999, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task DeletedStop_WhoseRebuildFailed_ReportsTheDeletionAsPersisted()
    {
        var survivors = new[] { new StopResponse { Id = 12, RouteId = 7, SequenceOrder = 1 } };
        var service = new TransportationStub
        {
            DeleteStopResult = new StopOrderResult(
                TransportWriteStatus.RoutingUnavailable, survivors, CurrentRoute, OrderPersisted: true),
        };
        var controller = WithUser(new StopsController(
            service, new PermissionStub(), NullLogger<StopsController>.Instance));

        var result = await controller.Delete(11, CancellationToken.None);

        // 503, but the client still has to learn the stop is gone and apply the renumbered survivors.
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.True(Property<bool>(objectResult.Value!, "deletePersisted"));
        Assert.Same(CurrentRoute, Property<RouteResponse>(objectResult.Value!, "route"));
        Assert.Same(survivors, Property<IReadOnlyList<StopResponse>>(objectResult.Value!, "stops"));
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

    private sealed class TransportationStub : ITransportationService
    {
        public RouteBuildResult BuildResult { get; init; } = RouteBuildResult.From(
            TransportWriteStatus.Success, CurrentRoute);
        public StopWriteResult StopResult { get; init; } = StopWriteResult.InvalidGeometry;
        public StopOrderResult OrderResult { get; init; } = StopOrderResult.InvalidOrder;
        public StopOrderResult DeleteStopResult { get; init; } = StopOrderResult.StopNotFound;
        public bool RouteDeleted { get; init; }
        // Proves a 403 short-circuits before the service is reached, rather than after a real delete.
        public int DeleteCallCount { get; private set; }

        public Task<StopWriteResult> CreateStopAsync(
            StopCreateRequest request,
            int userId,
            CancellationToken cancellationToken = default) => Task.FromResult(StopResult);

        public Task<StopOrderResult> ReorderStopsAsync(
            int routeId,
            IReadOnlyList<int> orderedStopIds,
            int userId,
            CancellationToken cancellationToken = default) => Task.FromResult(OrderResult);

        public Task<RouteBuildResult> RebuildRouteAsync(
            int routeId,
            int userId,
            CancellationToken cancellationToken = default) => Task.FromResult(BuildResult);

        public Task<DeleteStatus> DeleteRouteAsync(int id, int userId)
        {
            DeleteCallCount++;
            return Task.FromResult(RouteDeleted ? DeleteStatus.Success : DeleteStatus.NotFound);
        }

        public Task<StopOrderResult> DeleteStopAsync(
            int id,
            int userId,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            return Task.FromResult(DeleteStopResult);
        }

        public Task<IReadOnlyList<RouteResponse>> ListRoutesAsync() => throw new NotSupportedException();
        public Task<RouteWriteResult> CreateRouteAsync(RouteSaveRequest request, int userId) => throw new NotSupportedException();
        public Task<RouteWriteResult> UpdateRouteAsync(int id, RouteSaveRequest request, int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<StopResponse>?> ListRouteStopsAsync(int routeId) => throw new NotSupportedException();
        public Task<IReadOnlyList<StopResponse>> ListAllStopsAsync() => throw new NotSupportedException();
        public Task<StopWriteResult> UpdateStopAsync(int id, StopUpdateRequest request, int userId) => throw new NotSupportedException();
        public Task<AdminTransportationResponse> GetAdminSnapshotAsync() => throw new NotSupportedException();
    }

    private sealed class PermissionStub : IUserAdminService
    {
        // Default grants manage_transport; set false to stand in for an End User (Viewer).
        public bool Granted { get; init; } = true;

        public Task<bool> HasPermissionAsync(int userId, string permissionName) =>
            Task.FromResult(Granted && permissionName == SeedData.ManageTransportPermission);

        public Task<IReadOnlyList<AdminUserResponse>> ListAsync() => throw new NotSupportedException();
        public Task<AdminUserResponse?> GetAsync(int id) => throw new NotSupportedException();
        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> CreateAsync(AdminUserCreateRequest request) => throw new NotSupportedException();
        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> UpdateAsync(int id, AdminUserUpdateRequest request) => throw new NotSupportedException();
        public Task<AdminWriteStatus> DeleteAsync(int id) => throw new NotSupportedException();
        public Task<bool> SetRolesAsync(int userId, IReadOnlyList<int> roleIds) => throw new NotSupportedException();
        public Task<IReadOnlyList<EffectivePermissionResponse>?> GetEffectivePermissionsAsync(int userId) => throw new NotSupportedException();
        public Task<bool> SetDirectPermissionsAsync(int userId, IReadOnlyList<int> permissionIds) => throw new NotSupportedException();
        public Task<bool> IsAdminAsync(int userId) => throw new NotSupportedException();
        public Task<MeResponse?> GetMeAsync(int userId) => throw new NotSupportedException();
    }
}
