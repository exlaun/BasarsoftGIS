namespace Basarsoft.Api.DTOs;

// Result of the intersection-analysis query: the caller's private drawings plus shared POIs,
// transportation stops, and built routes that intersect the temporary polygon. A feature counts if
// it even slightly intersects the area (ST_Intersects) — full containment is not required.
public class AnalysisResponse
{
    public int Points { get; set; }
    public int Lines { get; set; }
    public int Polygons { get; set; }
    public int Pois { get; set; }
    public int Stops { get; set; }
    public int Routes { get; set; }
    public int Total { get; set; }
}
