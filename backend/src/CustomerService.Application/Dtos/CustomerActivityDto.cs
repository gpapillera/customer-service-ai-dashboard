namespace CustomerService.Application.Dtos;

/// <summary>One row in a customer's merged activity timeline (case events + account events).</summary>
public class CustomerActivityItemDto
{
    /// <summary>Monotonic id, used as the Angular track-by key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Event kind: opened | updated | resolved | log | comment | email
    /// (case-level) or account_invite | account_reset | account_activated
    /// (account-level). Mirrors the case-detail vocabulary extended for accounts.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Short label shown on the timeline row (e.g. "Invite sent").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Longer detail text for the row.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime AtUtc { get; set; }

    /// <summary>
    /// Related case id when the event belongs to a case (so the UI can
    /// deep-link to that case's history). Null for account-only events.
    /// </summary>
    public int? CaseId { get; set; }

    /// <summary>Optional secondary author/recipient shown beside the label.</summary>
    public string? Who { get; set; }
}

/// <summary>
/// A recorded "viewed/opened" audit event for a Case or Customer detail page.
/// Returned by <see cref="IViewEventService"/> and merged into activity panels.
/// </summary>
public class ViewEventDto
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>What was viewed: "Case" or "Customer".</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Id of the viewed Case or Customer.</summary>
    public int TargetId { get; set; }

    /// <summary>Viewer's user id (JWT sub) for staff; null for customer self-view.</summary>
    public string? ViewerUserId { get; set; }

    /// <summary>Human-readable viewer name shown on the timeline.</summary>
    public string ViewerName { get; set; } = string.Empty;

    /// <summary>Viewer role ("Admin"/"Agent"/"Customer"); null if unknown.</summary>
    public string? ViewerRole { get; set; }

    /// <summary>UTC timestamp of the view.</summary>
    public DateTime AtUtc { get; set; }
}
