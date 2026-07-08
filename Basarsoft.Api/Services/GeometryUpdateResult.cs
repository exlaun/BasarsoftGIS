using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// Outcome of a geometry write: it succeeded, the shape wasn't found (or isn't the caller's), the
// supplied WKT was invalid / the wrong geometry type, or the shape falls outside the caller's
// authorized drawing area. A plain nullable can't express these, so the service returns this and
// the controller maps each case to 200 / 404 / 400 / 403.
public enum UpdateStatus
{
    Success,
    NotFound,
    InvalidGeometry,
    OutsideAuthorizedArea,
}

public record GeometryUpdateResult(UpdateStatus Status, GeometryResponse? Response)
{
    public static readonly GeometryUpdateResult NotFound = new(UpdateStatus.NotFound, null);
    public static readonly GeometryUpdateResult InvalidGeometry = new(UpdateStatus.InvalidGeometry, null);
    public static readonly GeometryUpdateResult OutsideAuthorizedArea = new(UpdateStatus.OutsideAuthorizedArea, null);
    public static GeometryUpdateResult Ok(GeometryResponse response) => new(UpdateStatus.Success, response);
}
