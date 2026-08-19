namespace CustomerService.Domain.Entities;

/// <summary>
/// Represents a customer (person or company) that raises support cases.
/// Mirrors a Dynamics 365 / CRM account record.
/// </summary>
public class Customer
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Human-readable display ID (e.g. "CUST-00001"), generated after creation.</summary>
    public string? CustomerDisplayId { get; set; }

    /// <summary>Customer's full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Normalized email address (lowercase, trimmed).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Normalized phone number in E.164-ish format (digits, optional +).</summary>
    public string? Phone { get; set; }

    /// <summary>Company / account the customer belongs to (optional).</summary>
    public string? Company { get; set; }

    /// <summary>Free-text address line.</summary>
    public string? Address { get; set; }

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the last account-level profile edit (name/email/phone/
    /// company/address). Null until the first edit. Used by the Customers
    /// sidenav badge to flag "info updated since I last looked" — distinct from
    /// case-level activity (which is surfaced in the notification center, not here).
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Soft-delete flag. When true, the customer is hidden from normal queries but retained.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp when the record was soft-deleted. Null until deleted.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>ID of the user who soft-deleted the record. Null until deleted.</summary>
    public string? DeletedById { get; set; }

    /// <summary>Hard-purge flag. When true, the record has been anonymized (PII scrubbed, row kept) and is irreversible. See Plan Task A8.</summary>
    public bool Purged { get; set; }

    /// <summary>UTC timestamp when the record was purged. Null until purged.</summary>
    public DateTime? PurgedAtUtc { get; set; }

    /// <summary>ID of the user who restored the record. Null until restored.</summary>
    public string? RestoredById { get; set; }

    /// <summary>UTC timestamp when the record was restored from soft-delete. Null until restored.</summary>
    public DateTime? RestoredAtUtc { get; set; }

    /// <summary>Navigation property: cases raised by this customer.</summary>
    public ICollection<Case> Cases { get; set; } = new List<Case>();

    /// <summary>Navigation property: 1:1 account record (invite/password state).</summary>
    public CustomerAccount? Account { get; set; }
}
