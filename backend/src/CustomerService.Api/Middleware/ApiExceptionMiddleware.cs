using System.Net;
using System.Text.Json;
using CustomerService.Domain.Entities;

namespace CustomerService.Api.Middleware;

/// <summary>
/// API error envelope returned by <see cref="ApiExceptionMiddleware"/> so the
/// frontend always receives a consistent JSON shape (never a raw stack trace).
/// </summary>
public class ApiErrorResponse
{
    /// <summary>Human-readable message (safe to show the user).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional machine-readable error code.</summary>
    public string? Code { get; set; }

    /// <summary>HTTP status code that produced this error.</summary>
    public int Status { get; set; }

    /// <summary>Trace identifier for correlating server logs.</summary>
    public string TraceId { get; set; } = string.Empty;
}

/// <summary>
/// Global exception-handling middleware. Catches unhandled exceptions and
/// converts them into a consistent JSON error envelope. Known exception types
/// (e.g. <see cref="KeyNotFoundException"/>) map to the appropriate HTTP
/// status; everything else becomes a 500 without leaking internals.
/// </summary>
public class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    /// <summary>Initializes a new <see cref="ApiExceptionMiddleware"/>.</summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for server-side diagnostics.</param>
    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Invokes the middleware, wrapping the request in a try/catch.</summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var status = ex switch
        {
            KeyNotFoundException => HttpStatusCode.NotFound,
            ArgumentException or InvalidOperationException => HttpStatusCode.BadRequest,
            UnauthorizedAccessException => HttpStatusCode.Forbidden,
            _ => HttpStatusCode.InternalServerError,
        };

        // Log the full exception server-side; only a safe message goes to client.
        _logger.LogError(ex, "Unhandled exception (Status {StatusCode}): {Message}", (int)status, ex.Message);

        var payload = new ApiErrorResponse
        {
            Message = status == HttpStatusCode.InternalServerError
                ? "An unexpected error occurred. Please try again later."
                : ex.Message,
            Code = ex.GetType().Name,
            Status = (int)status,
            TraceId = context.TraceIdentifier,
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }
}
