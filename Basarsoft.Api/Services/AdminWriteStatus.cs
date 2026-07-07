namespace Basarsoft.Api.Services;

// Outcome of an admin create/update that can fail on either a missing target or a duplicate name.
// Controllers map Ok -> 200, NotFound -> 404, Conflict -> 409.
public enum AdminWriteStatus
{
    Ok,
    NotFound,
    Conflict,
}
