namespace Basarsoft.Api.Services;

// Outcome of a soft delete. A plain bool can only say found/not-found, which cannot express a
// delete refused because the feature sits outside the caller's authorized area, so the delete
// services return this instead and each controller maps it to 204 / 404 / 403. Mirrors the
// status-enum pattern used by UpdateStatus and TransportWriteStatus.
public enum DeleteStatus
{
    Success,
    NotFound,
    OutsideAuthorizedArea,
    SimulationRunning,
}
