namespace CustomerService.Domain.Entities;

/// <summary>
/// Explicit audit row for customer-account activity that is NOT derivable from
/// the case graph or Notification table — today: profile/account field edits
/// made by staff or by the customer themselves. Keeps the Emails panels clean
/// while feeding the Activity panel and the customer card's "recent activity".
/// </summary>
public class CustomerActivity
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Customer this activity belongs to.</summary>
    public int CustomerId { get; set; }

    /// <summary>Navigation property back to the customer.</summary>
    public Customer? Customer { get; set; }

    /// <summary>Event kind, e.g. "account_updated". Mirrors the activity vocab.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Short label shown on the timeline (e.g. "Profile updated").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Detail text (e.g. "Changed: name, email").</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Staff actor id when edited by staff; null for customer self-edit.</summary>
    public string? ActorUserId { get; set; }

    /// <summary>Actor role ("Admin"/"Agent"/"Customer"); null if unknown.</summary>
    public string? ActorRole { get; set; }
}
