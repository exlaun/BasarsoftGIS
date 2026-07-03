namespace Basarsoft.Api.DTOs;

// Result of the inventory-analysis query: how many of the caller's shapes intersect the temporary
// polygon, broken down by type plus a total. A shape counts if it even slightly intersects the area
// (ST_Intersects) — full containment is not required.
public class AnalysisResponse
{
    public int Points { get; set; }
    public int Lines { get; set; }
    public int Polygons { get; set; }
    public int Total { get; set; }
}
