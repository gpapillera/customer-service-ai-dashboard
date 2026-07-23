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

    /// <summary>Navigation property: cases raised by this customer.</summary>
    public ICollection<Case> Cases { get; set; } = new List<Case>();

    /// <summary>Navigation property: 1:1 account record (invite/password state).</summary>
    public CustomerAccount? Account { get; set; }
}
