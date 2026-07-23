namespace Basarsoft.Api.Settings;

public class RouteSimulationSettings
{
    public const string SectionName = "RouteSimulation";

    public int UpdateIntervalMilliseconds { get; set; } = 1000;

    // 60 means one minute of simulated driving elapses per wall-clock second.
    public double TimeScale { get; set; } = 60;

    // Used only when a legacy route has geometry but no usable OSRM duration/distance metrics.
    public double FallbackSpeedMetersPerSecond { get; set; } = 13.89;
}
