namespace CustomerService.Api.Middleware;

/// <summary>
/// Applies baseline security response headers to every API response. These are
/// low-risk, high-value hardening headers that don't depend on the client app:
/// - X-Content-Type-Options: nosniff  (stops MIME sniffing)
/// - X-Frame-Options: DENY           (prevents clickjacking of the API)
/// - Referrer-Policy: no-referrer    (limits what the API URL leaks to third parties)
/// A full CSP is intentionally deferred: the Angular SPA's asset/CSP needs are a
/// frontend concern (Phase C) and would require coordinated tuning to avoid
/// breaking the client overfetch.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes a new <see cref="SecurityHeadersMiddleware"/>.</summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Invokes the middleware, stamping headers before the response is sent.</summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        await _next(context);
    }
}
