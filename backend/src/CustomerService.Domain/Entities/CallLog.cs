namespace CustomerService.Domain.Entities;

/// <summary>
/// A call or follow-up log entry attached to a case. Records agent/customer contact.
/// </summary>
public class CallLog
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the parent case.</summary>
    public int CaseId { get; set; }

    /// <summary>Navigation property to the parent case.</summary>
    public Case? Case { get; set; }

    /// <summary>Direction of the contact.</summary>
    public CallDirection Direction { get; set; } = CallDirection.Outbound;

    /// <summary>Free-text notes about the call / follow-up.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Duration of the call in seconds (0 for non-call follow-ups).</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Agent who logged the entry.</summary>
    public string? LoggedByUserId { get; set; }

    /// <summary>UTC timestamp when the log was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Direction of a call / follow-up log entry.
/// </summary>
public enum CallDirection
{
    /// <summary>Agent called the customer.</summary>
    Inbound = 0,

    /// <summary>Customer called in.</summary>
    Outbound = 1,
}
