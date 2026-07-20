namespace CustomerService.Domain.Entities;

/// <summary>
/// Holds authentication state for a <see cref="Customer"/>, kept separate
/// from the profile data on <see cref="Customer"/>. A customer has no login
/// until an invite is accepted and a password is set.
/// </summary>
public class CustomerAccount
{
    /// <summary>Primary key (matches <see cref="Customer.Id"/>).</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Customer"/> (1:1).</summary>
    public int CustomerId { get; set; }

    /// <summary>Navigation property to the owning customer.</summary>
    public Customer? Customer { get; set; }

    /// <summary>
    /// BCrypt hash of the customer's password. Null until the invite is
    /// accepted and a password is chosen. Never stored in plaintext.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Cryptographically random invite token (e.g. a GUID). Null once used or
    /// if no invite has been issued. Not sequential or guessable.
    /// </summary>
    public string? InviteToken { get; set; }

    /// <summary>UTC expiry for the current invite token (48h after issue).</summary>
    public DateTime? InviteTokenExpiresAt { get; set; }

    /// <summary>True once the invite token has been consumed.</summary>
    public bool InviteTokenUsed { get; set; }

    /// <summary>
    /// True once the invite is accepted and a password is set. Gates login.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>UTC timestamp when the account record was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
