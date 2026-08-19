using CustomerService.Domain.Entities;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// Persisted refresh-token store. Refresh tokens are opaque random strings
/// stored server-side so they can be rotated (old revoked on use) and revoked
/// (on logout). Access JWTs stay stateless; only the long-lived refresh
/// carries server state.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Creates and stores a fresh refresh token. Returns the opaque token value
    /// (to be set in an HttpOnly cookie).
    /// </summary>
    Task<string> CreateAsync(string subjectId, string subjectType, string role, int daysValid);

    /// <summary>
    /// Validates a refresh token: must exist, not be revoked, and not be
    /// expired. Returns the stored row on success.
    /// </summary>
    Task<(bool Ok, RefreshToken? Token)> ValidateAsync(string token);

    /// <summary>
    /// Rotates a refresh token: revokes the old one (records the replacement)
    /// and issues a new one for the same subject. Returns the new opaque token.
    /// </summary>
    Task<string> RotateAsync(string oldToken);

    /// <summary>Revokes a refresh token (e.g. on logout). Idempotent.</summary>
    Task RevokeAsync(string token);
}
