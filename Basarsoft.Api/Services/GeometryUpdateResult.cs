using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// Three-way outcome of an update: it succeeded, the shape wasn't found (or isn't the caller's), or
// the supplied WKT was invalid / the wrong geometry type. A plain nullable can't express all three,
// so the service returns this and the controller maps each case to 200 / 404 / 400.
public enum UpdateStatus
{
    Success,
    NotFound,
    InvalidGeometry,
}

public record GeometryUpdateResult(UpdateStatus Status, GeometryResponse? Response)
{
    public static readonly GeometryUpdateResult NotFound = new(UpdateStatus.NotFound, null);
    public static readonly GeometryUpdateResult InvalidGeometry = new(UpdateStatus.InvalidGeometry, null);
    public static GeometryUpdateResult Ok(GeometryResponse response) => new(UpdateStatus.Success, response);
}
