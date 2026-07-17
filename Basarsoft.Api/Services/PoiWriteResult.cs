using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// Outcome of a POI create. Mirrors GeometryUpdateResult's shape so PoiController can map each case
// the same way: 200 / 400 (bad WKT or unknown category) / 403 (outside the caller's authorized area).
public enum PoiWriteStatus
{
    Success,
    InvalidGeometry,
    CategoryNotFound,
    OutsideAuthorizedArea,
}

public record PoiWriteResult(PoiWriteStatus Status, PoiResponse? Response)
{
    public static readonly PoiWriteResult InvalidGeometry = new(PoiWriteStatus.InvalidGeometry, null);
    public static readonly PoiWriteResult CategoryNotFound = new(PoiWriteStatus.CategoryNotFound, null);
    public static readonly PoiWriteResult OutsideAuthorizedArea = new(PoiWriteStatus.OutsideAuthorizedArea, null);
    public static PoiWriteResult Ok(PoiResponse response) => new(PoiWriteStatus.Success, response);
}

// Outcome of a category write/delete. Conflict = duplicate name under the same parent (409);
// InvalidParent = missing parent or a parent that would create a cycle (400); InUse = the category
// still has children or POIs, so deleting it would orphan them (409).
public enum PoiCategoryWriteStatus
{
    Ok,
    NotFound,
    Conflict,
    InvalidParent,
    InvalidIcon,
    InUse,
}

public record PoiCategoryWriteResult(PoiCategoryWriteStatus Status, PoiCategoryResponse? Response)
{
    public static readonly PoiCategoryWriteResult NotFound = new(PoiCategoryWriteStatus.NotFound, null);
    public static readonly PoiCategoryWriteResult Conflict = new(PoiCategoryWriteStatus.Conflict, null);
    public static readonly PoiCategoryWriteResult InvalidParent = new(PoiCategoryWriteStatus.InvalidParent, null);
    public static readonly PoiCategoryWriteResult InvalidIcon = new(PoiCategoryWriteStatus.InvalidIcon, null);
    public static readonly PoiCategoryWriteResult InUse = new(PoiCategoryWriteStatus.InUse, null);
    public static PoiCategoryWriteResult Ok(PoiCategoryResponse response) => new(PoiCategoryWriteStatus.Ok, response);
}
