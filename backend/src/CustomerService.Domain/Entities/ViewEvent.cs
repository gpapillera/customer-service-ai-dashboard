namespace CustomerService.Domain.Entities;

/// <summary>
/// Audit row recording that a user opened/viewed a Case or a Customer detail
/// page. Distinct from <see cref="CustomerActivity"/>, which tracks account
/// field edits: a view is purely a read event, coalesced per viewer by a
/// cooldown (see <c>ViewEventService</c>) so page refreshes don't flood the
/// timeline. Stored as a discriminator table (TargetType + TargetId) rather
/// than two FKs so the log survives target deletion and needs no migration.
/// </summary>
public class ViewEvent
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
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
