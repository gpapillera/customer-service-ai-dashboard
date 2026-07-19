using CustomerService.Domain.Entities;

namespace CustomerService.Domain.Interfaces;

/// <summary>
/// Lightweight summary of a case whose scheduled follow-up is overdue. Used by
/// the dashboard to surface cases that need attention.
/// </summary>
public class OverdueFollowUpSummary
{
    /// <summary>Case id.</summary>
    public int CaseId { get; set; }

    /// <summary>Case subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Customer name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Assigned agent name (or empty if unassigned).</summary>
    public string AssignedToUserName { get; set; } = string.Empty;

    /// <summary>Priority of the overdue case.</summary>
    public Priority Priority { get; set; }

    /// <summary>The follow-up deadline that has passed (UTC).</summary>
    public DateTime FollowUpDueUtc { get; set; }

    /// <summary>Number of days past the deadline (rounded up, minimum 1).</summary>
    public int DaysOverdue { get; set; }
}
