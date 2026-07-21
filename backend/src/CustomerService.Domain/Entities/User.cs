namespace CustomerService.Domain.Entities;

/// <summary>
/// Represents an application user (agent or admin) who can authenticate and act on cases.
/// </summary>
public class User
{
    /// <summary>Primary key (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Login username, unique.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Display name shown in the UI.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Hashed password (BCrypt / PBKDF2 — never stored in plaintext).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Role assigned to the user (Admin or Agent).</summary>
    public UserRole Role { get; set; } = UserRole.Agent;

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ── Staff password-reset (independent of customer invite token fields) ──

    /// <summary>
    /// Opaque token for staff password-reset. Distinctly named from anything
    /// invite-related because staff accounts are seeded directly and never go
    /// through an invite/activation step.
    /// </summary>
    public string? ResetToken { get; set; }

    /// <summary>UTC expiry for <see cref="ResetToken"/> (48-hour window).</summary>
    public DateTime? ResetTokenExpiresAt { get; set; }

    /// <summary>Whether this token has already been consumed.</summary>
    public bool ResetTokenUsed { get; set; }
}

/// <summary>
/// Roles supported by the dashboard's JWT authorization.
/// </summary>
public enum UserRole
{
    /// <summary>Standard agent: can manage customers, cases and call logs.</summary>
    Agent = 0,

    /// <summary>Administrator: full access including user/seed management.</summary>
    Admin = 1,
}
