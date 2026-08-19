namespace CustomerService.Domain.Entities;

/// <summary>
/// Server-side refresh token. Unlike the short-lived access JWT (which is
/// stateless), a refresh token is persisted so it can be rotated and revoked.
/// Stored hashed-by-reference via a random opaque string (the Token column holds
/// the raw value — it is a random 64-byte string, never a password, and is only
/// ever compared in full; rotation revokes the prior value).
/// </summary>
public class RefreshToken
{
    /// <summary>Primary key (DB-generated).</summary>
    public int Id { get; set; }

    /// <summary>
    /// The opaque random token returned to the client in an HttpOnly cookie.
    /// Unique; used as the lookup key for validation + rotation.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The subject's stable id (User.Id for staff, Customer.Id for customers).</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Discriminator: "Staff" or "Customer".</summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>The role at issuance time; copied onto the new access token on rotation.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>UTC creation time.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC absolute expiry.</summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>UTC revocation time, or null if still active.</summary>
    public DateTime? RevokedUtc { get; set; }

    /// <summary>
    /// Token that replaced this one during rotation. Lets us detect reuse of an
    /// already-rotated token (a sign of token theft) by following the chain.
    /// </summary>
    public string? ReplacedByToken { get; set; }
}
