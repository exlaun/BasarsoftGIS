namespace Basarsoft.Api.Services;

// Immutable, start-time route snapshot. Distances use Haversine metres while interpolation follows
// every persisted OSRM polyline segment, never the straight lines between stops.
public sealed class RouteSimulationPath
{
    private const double EarthRadiusMeters = 6_371_008.8;
    private readonly (double Longitude, double Latitude)[] _coordinates;
    private readonly double[] _cumulativeDistances;
    private readonly RouteSimulationStopSnapshot[] _stops;
    private readonly double[] _stopRouteDistances;

    private RouteSimulationPath(
        (double Longitude, double Latitude)[] coordinates,
        double[] cumulativeDistances,
        RouteSimulationStopSnapshot[] stops,
        double[] stopRouteDistances)
    {
        _coordinates = coordinates;
        _cumulativeDistances = cumulativeDistances;
        _stops = stops;
        _stopRouteDistances = stopRouteDistances;
        TotalDistanceMeters = cumulativeDistances[^1];
    }

    public double TotalDistanceMeters { get; }
    public int StopCount => _stops.Length;
    public (double Longitude, double Latitude) FirstStop =>
        (_stops[0].Longitude, _stops[0].Latitude);
    public (double Longitude, double Latitude) FinalStop =>
        (_stops[^1].Longitude, _stops[^1].Latitude);

    public static bool TryCreate(RouteSimulationRouteSnapshot snapshot, out RouteSimulationPath? path)
    {
        path = null;
        if (snapshot.Stops.Count < 2 || snapshot.GeometryCoordinates.Count < 2)
            return false;

        var coordinates = snapshot.GeometryCoordinates.ToArray();
        var stops = snapshot.Stops.ToArray();
        if (coordinates.Any(point => !IsValidCoordinate(point.Longitude, point.Latitude)) ||
            stops.Any(stop => !IsValidCoordinate(stop.Longitude, stop.Latitude)))
            return false;

        var cumulative = new double[coordinates.Length];
        for (var i = 1; i < coordinates.Length; i++)
        {
            cumulative[i] = cumulative[i - 1] + DistanceMeters(coordinates[i - 1], coordinates[i]);
            if (!double.IsFinite(cumulative[i]))
                return false;
        }

        if (cumulative[^1] <= 0)
            return false;

        var stopRouteDistances = LocateStopsAlongRoute(coordinates, cumulative, stops);
        path = new RouteSimulationPath(coordinates, cumulative, stops, stopRouteDistances);
        return true;
    }

    public RouteSimulationPathPosition PositionAtProgress(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0)
            return AtExactStop(0, 0);
        if (progress >= 1)
            return AtExactStop(_stops.Length - 1, 100);

        var traveled = TotalDistanceMeters * progress;
        var segment = Array.BinarySearch(_cumulativeDistances, traveled);
        if (segment < 0)
            segment = ~segment;
        segment = Math.Clamp(segment, 1, _coordinates.Length - 1);

        var segmentStart = _cumulativeDistances[segment - 1];
        var segmentLength = _cumulativeDistances[segment] - segmentStart;
        var fraction = segmentLength <= 0 ? 0 : (traveled - segmentStart) / segmentLength;
        var before = _coordinates[segment - 1];
        var after = _coordinates[segment];
        var longitude = before.Longitude + ((after.Longitude - before.Longitude) * fraction);
        var latitude = before.Latitude + ((after.Latitude - before.Latitude) * fraction);
        var nearest = NearestStopIndex(traveled);

        return new RouteSimulationPathPosition(
            longitude,
            latitude,
            progress * 100,
            nearest,
            _stops[nearest].Name);
    }

    private RouteSimulationPathPosition AtExactStop(int index, double progressPercent) => new(
        _stops[index].Longitude,
        _stops[index].Latitude,
        progressPercent,
        index,
        _stops[index].Name);

    private int NearestStopIndex(double traveled)
    {
        var nearest = 0;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < _stops.Length; i++)
        {
            var distance = Math.Abs(traveled - _stopRouteDistances[i]);
            if (distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    // Project each ordered stop onto the persisted route and record its cumulative route distance.
    // Searching only at/after the prior stop's measure preserves stop order at loops and crossings.
    // The first and last stops are the route endpoints by the OSRM contract.
    private static double[] LocateStopsAlongRoute(
        (double Longitude, double Latitude)[] coordinates,
        double[] cumulativeDistances,
        RouteSimulationStopSnapshot[] stops)
    {
        var measures = new double[stops.Length];
        measures[0] = 0;
        measures[^1] = cumulativeDistances[^1];
        var minimumMeasure = 0d;

        for (var stopIndex = 1; stopIndex < stops.Length - 1; stopIndex++)
        {
            var stop = (stops[stopIndex].Longitude, stops[stopIndex].Latitude);
            var bestDistanceSquared = double.MaxValue;
            var bestMeasure = minimumMeasure;

            for (var segment = 1; segment < coordinates.Length; segment++)
            {
                var segmentEndMeasure = cumulativeDistances[segment];
                if (segmentEndMeasure < minimumMeasure)
                    continue;

                var segmentStartMeasure = cumulativeDistances[segment - 1];
                var segmentLength = segmentEndMeasure - segmentStartMeasure;
                if (segmentLength <= 0)
                    continue;

                var projectionFraction = ProjectToSegment(stop, coordinates[segment - 1], coordinates[segment]);
                var minimumFraction = segmentStartMeasure >= minimumMeasure
                    ? 0
                    : (minimumMeasure - segmentStartMeasure) / segmentLength;
                var fraction = Math.Clamp(projectionFraction, minimumFraction, 1);
                var distanceSquared = ProjectionDistanceSquared(
                    stop,
                    coordinates[segment - 1],
                    coordinates[segment],
                    fraction);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestMeasure = segmentStartMeasure + (segmentLength * fraction);
                }
            }

            measures[stopIndex] = bestMeasure;
            minimumMeasure = bestMeasure;
        }

        return measures;
    }

    // Equirectangular local projection is stable for short OSRM segments and gives the fraction along
    // the segment; cumulative route measures themselves continue to use Haversine metres.
    private static double ProjectToSegment(
        (double Longitude, double Latitude) point,
        (double Longitude, double Latitude) start,
        (double Longitude, double Latitude) end)
    {
        var meanLatitude = DegreesToRadians((start.Latitude + end.Latitude + point.Latitude) / 3d);
        var cosLatitude = Math.Cos(meanLatitude);
        var segmentX = DegreesToRadians(end.Longitude - start.Longitude) * cosLatitude * EarthRadiusMeters;
        var segmentY = DegreesToRadians(end.Latitude - start.Latitude) * EarthRadiusMeters;
        var pointX = DegreesToRadians(point.Longitude - start.Longitude) * cosLatitude * EarthRadiusMeters;
        var pointY = DegreesToRadians(point.Latitude - start.Latitude) * EarthRadiusMeters;
        var lengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        var fraction = lengthSquared <= 0
            ? 0
            : Math.Clamp(((pointX * segmentX) + (pointY * segmentY)) / lengthSquared, 0, 1);
        return fraction;
    }

    private static double ProjectionDistanceSquared(
        (double Longitude, double Latitude) point,
        (double Longitude, double Latitude) start,
        (double Longitude, double Latitude) end,
        double fraction)
    {
        var projected = (
            Longitude: start.Longitude + ((end.Longitude - start.Longitude) * fraction),
            Latitude: start.Latitude + ((end.Latitude - start.Latitude) * fraction));
        var distance = DistanceMeters(point, projected);
        return distance * distance;
    }

    private static bool IsValidCoordinate(double longitude, double latitude) =>
        double.IsFinite(longitude) && double.IsFinite(latitude) &&
        longitude is >= -180 and <= 180 && latitude is >= -90 and <= 90;

    private static double DistanceMeters(
        (double Longitude, double Latitude) from,
        (double Longitude, double Latitude) to)
    {
        var lat1 = DegreesToRadians(from.Latitude);
        var lat2 = DegreesToRadians(to.Latitude);
        var deltaLatitude = lat2 - lat1;
        var deltaLongitude = DegreesToRadians(to.Longitude - from.Longitude);
        var a = Math.Pow(Math.Sin(deltaLatitude / 2), 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLongitude / 2), 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}

public record RouteSimulationPathPosition(
    double Longitude,
    double Latitude,
    double ProgressPercent,
    int CurrentStopIndex,
    string? CurrentStopName);
