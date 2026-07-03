using Microsoft.AspNetCore.Diagnostics;

namespace Basarsoft.Api.Middleware;

// Application-wide safety net for unhandled exceptions. The controllers each wrap their own logic in
// try-catch (per the task requirement), and this handler is the final backstop: it catches anything
// that still escapes — e.g. a failure in middleware or a code path that forgot to wrap itself — and
// turns it into a clean 500 JSON response instead of leaking a stack trace to the client. The real
// exception is logged server-side; the client only ever sees a generic message.
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        // Same { message } body shape the controllers use for all their error responses, so the 500
        // contract is consistent no matter where the failure is caught.
        await httpContext.Response.WriteAsJsonAsync(
            new { message = "An unexpected error occurred." }, cancellationToken);

        return true; // handled — stop the exception from propagating further
    }
}
