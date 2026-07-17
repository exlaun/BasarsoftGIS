namespace Basarsoft.Api.Models;

// One weighted criterion of a location analysis -> tbl_location_analysis_criterion. Each row scores a
// POI category (main OR sub — a criterion on a parent also matches every descendant category's POIs)
// out of 100; the service guarantees a run holds 2..5 rows whose weights sum to exactly 100.
public class LocationAnalysisCriterion : IAuditable
{
    public int Id { get; set; }

    // FK -> tbl_location_analysis.id (cascade: criteria are meaningless without their run).
    public int AnalysisId { get; set; }

    // FK -> tbl_poi_category.id. Restrict-on-delete like tbl_poi's category FK.
    public int CategoryId { get; set; }

    // Importance score 1..100 (DB check constraint). All weights of one analysis sum to 100.
    public int Weight { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public int? ModifiedUserId { get; set; }

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
