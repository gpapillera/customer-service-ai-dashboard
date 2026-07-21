namespace CustomerService.Domain;

/// <summary>
/// Raised when an authenticated caller attempts an action they are not allowed
/// to perform (e.g. an Agent trying to edit a case assigned to another agent,
/// or view a customer they don't share a case with). Maps to HTTP 403
/// Forbidden by <see cref="Api.Middleware.ApiExceptionMiddleware"/>.
/// </summary>
public class ForbiddenException : Exception
{
    /// <summary>Initializes a new <see cref="ForbiddenException"/>.</summary>
    /// <param name="message">Human-readable reason (safe to surface to the user).</param>
    public ForbiddenException(string message) : base(message) { }
}
