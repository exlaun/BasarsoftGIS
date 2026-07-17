using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// Outcome of a location-analysis create. Mirrors PoiWriteResult's shape so the controller maps each
// case to a 400 with a machine-readable `code` (all of these are client mistakes, never server faults).
public enum LocationAnalysisWriteStatus
{
    Success,
    // Zero or both of provinceId/regionWkt were sent — the region must come from exactly one source.
    RegionRequired,
    // regionWkt didn't parse, or parsed to something other than a non-empty (Multi)Polygon.
    InvalidGeometry,
    ProvinceNotFound,
    // The same category appears in two criteria.
    DuplicateCategory,
    CategoryNotFound,
    // The mentor's hard rule: weights must total exactly 100 or the analysis must not start.
    WeightSumInvalid,
}

public record LocationAnalysisWriteResult(LocationAnalysisWriteStatus Status, LocationAnalysisResponse? Response)
{
    public static readonly LocationAnalysisWriteResult RegionRequired = new(LocationAnalysisWriteStatus.RegionRequired, null);
    public static readonly LocationAnalysisWriteResult InvalidGeometry = new(LocationAnalysisWriteStatus.InvalidGeometry, null);
    public static readonly LocationAnalysisWriteResult ProvinceNotFound = new(LocationAnalysisWriteStatus.ProvinceNotFound, null);
    public static readonly LocationAnalysisWriteResult DuplicateCategory = new(LocationAnalysisWriteStatus.DuplicateCategory, null);
    public static readonly LocationAnalysisWriteResult CategoryNotFound = new(LocationAnalysisWriteStatus.CategoryNotFound, null);
    public static readonly LocationAnalysisWriteResult WeightSumInvalid = new(LocationAnalysisWriteStatus.WeightSumInvalid, null);
    public static LocationAnalysisWriteResult Ok(LocationAnalysisResponse response) => new(LocationAnalysisWriteStatus.Success, response);
}
