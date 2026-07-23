using System.Text.Json.Serialization;

namespace Basarsoft.Api.DTOs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RouteSimulationStatus
{
    NotStarted,
    Running,
    Completed,
    Stopped,
    Failed,
}

// The authoritative live state returned by REST and sent through SignalR. Sequence is monotonic
// within a run; RunId changes on restart so clients can reject late messages from the prior run.
public class RouteSimulationResponse
{
    public Guid? RunId { get; set; }
    public int RouteId { get; set; }
    public RouteSimulationStatus Status { get; set; }
    public double? Longitude { get; set; }
    public double? Latitude { get; set; }
    public double ProgressPercent { get; set; }
    public int? CurrentStopIndex { get; set; }
    public string? CurrentStopName { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Sequence { get; set; }
    public string? FailureCode { get; set; }
}
