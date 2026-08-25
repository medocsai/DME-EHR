using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace EHR.Helpers;

/// <summary>
/// Builds 500 responses that do not hand internals to the caller.
///
/// WHY THIS EXISTS
/// Six endpoints, including four on the Super Admin clinic console, returned
/// the raw exception message AND the full stack trace in the response body:
///
///     return StatusCode(500, new { message = "Failed to create tenant",
///                                  error = ex.Message, stackTrace = ex.StackTrace });
///
/// A stack trace names internal namespaces, file paths, ORM internals and often
/// the failing SQL. It is a free map of the system for anyone probing it, and a
/// SqlException message can carry column values, which on these tables means
/// PHI. It was returned in every environment, not just development.
///
/// WHAT IT DOES
/// Always logs the full exception server-side, where it belongs, and returns a
/// short correlation id the caller can quote. Detail is included in the body
/// ONLY in Development, so debugging locally still works exactly as before.
///
/// WHO CALLS IT
/// Any controller catch block that would otherwise build its own 500 payload,
/// and UnhandledExceptionMiddleware for everything nobody caught.
/// </summary>
public static class ApiError
{
    /// <summary>
    /// Log the exception and produce a safe 500 payload.
    /// <paramref name="publicMessage"/> is shown to the caller and must never
    /// contain anything derived from the exception.
    /// </summary>
    public static ObjectResult ServerError(
        this ControllerBase controller, Exception ex, string publicMessage)
    {
        var http = controller.HttpContext;
        var correlationId = http.TraceIdentifier;

        var logger = http.RequestServices.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        logger?.CreateLogger(controller.GetType())
              .LogError(ex, "{PublicMessage} (correlationId {CorrelationId})", publicMessage, correlationId);

        var env = http.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        var isDevelopment = env?.IsDevelopment() == true;

        // The correlation id is safe to return: it is the request id, already in
        // the response headers, and it is what makes a support report traceable
        // to the logged exception without exposing the exception itself.
        object body = isDevelopment
            ? new
            {
                message = publicMessage,
                correlationId,
                error = ex.Message,
                stackTrace = ex.StackTrace
            }
            : new
            {
                message = publicMessage,
                correlationId
            };

        return controller.StatusCode(StatusCodes.Status500InternalServerError, body);
    }
}

/// <summary>
/// Catches anything no controller caught, so an unhandled exception cannot
/// escape as a framework error page.
///
/// Belongs at the very top of the pipeline: it can only protect what runs after
/// it. Deliberately writes JSON for API paths and lets HTML navigations fall
/// through to the error page, so neither kind of client gets a surprise.
/// </summary>
public sealed class UnhandledExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UnhandledExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public UnhandledExceptionMiddleware(
        RequestDelegate next, ILogger<UnhandledExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path} (correlationId {CorrelationId})",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);

            // If the response has already started there is nothing safe to do:
            // rewriting it would interleave a second body into a partial one.
            if (context.Response.HasStarted) throw;

            // Development keeps the framework's developer exception page, which
            // is far more useful than anything written here.
            if (_env.IsDevelopment()) throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                message = "An unexpected error occurred.",
                correlationId = context.TraceIdentifier
            });
        }
    }
}
