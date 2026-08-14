namespace CustomerService.Domain.Entities;

/// <summary>
/// Lifecycle status of a support case.
/// See docs/DIY.md §3 — serialized as a string in JSON, never a number.
/// </summary>
public enum CaseStatus
{
    /// <summary>Newly created, not yet worked.</summary>
    New = 0,

    /// <summary>Agent is actively working the case.</summary>
    InProgress = 1,

    /// <summary>Escalated to a higher tier or manager.</summary>
    Escalated = 2,

    /// <summary>Issue resolved, pending closure.</summary>
    Resolved = 3,

    /// <summary>Resolved and closed.</summary>
    Closed = 4,
}

/// <summary>
/// Predicted / assigned priority of a case. The ML model suggests this value,
/// but an agent may override it.
/// </summary>
public enum Priority
{
    /// <summary>Low urgency.</summary>
    Low = 0,

    /// <summary>Medium urgency.</summary>
    Medium = 1,

    /// <summary>High urgency — needs fast attention.</summary>
    High = 2,
}

/// <summary>
/// A customer service case (ticket). The central entity of the dashboard.
/// </summary>
public class Case
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Short subject/title of the case.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Longer description of the issue.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Current lifecycle status.</summary>
    public CaseStatus Status { get; set; } = CaseStatus.New;

    /// <summary>Priority assigned to the case (may be ML-suggested or manually overridden).</summary>
    public Priority Priority { get; set; } = Priority.Medium;

    /// <summary>Foreign key to the owning customer.</summary>
    public int CustomerId { get; set; }

    /// <summary>Navigation property to the owning customer.</summary>
    public Customer? Customer { get; set; }

    /// <summary>Foreign key to the case category.</summary>
    public int CategoryId { get; set; }

    /// <summary>Navigation property to the category.</summary>
    public Category? Category { get; set; }

    /// <summary>Foreign key to the agent who owns the case (nullable).</summary>
    public string? AssignedToUserId { get; set; }

    /// <summary>Navigation property to the assigned agent.</summary>
    public User? AssignedToUser { get; set; }

    /// <summary>True when the priority was set by the ML model rather than a human.</summary>
    public bool PriorityAutoSuggested { get; set; }

    /// <summary>Plain-English reason for the ML-suggested priority (when auto-suggested).</summary>
    public string? PriorityReason { get; set; }

    /// <summary>Human-readable display ID (e.g. "CAS-00042"), auto-generated after creation.</summary>
    public string? CaseDisplayId { get; set; }

    /// <summary>UTC timestamp when the case was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last status change.</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the case first reached a resolved/closed state. Null
    /// while the case is still open. Surfaced to customers (read-only) so they
    /// can see when their issue was resolved.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>UTC timestamp of the customer's last contact before this case (for ML feature).</summary>
    public DateTime? LastContactUtc { get; set; }

    /// <summary>
    /// UTC deadline by which the next follow-up (call/log) should be completed.
    /// When this is in the past and the case is still open with no follow-up
    /// since the deadline, the case is considered to have an overdue follow-up.
    /// Null means no follow-up is scheduled.
    /// </summary>
    public DateTime? FollowUpDueUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent assignment to an agent. Null while the
    /// case is unassigned. Set on create when an assignee is supplied and on
    /// every (re)assignment in UpdateAsync; cleared on unassign. Drives the
    /// "cases assigned to me since I last looked" nav badge.
    /// </summary>
    public DateTime? AssignedAtUtc { get; set; }

    /// <summary>
    /// ponytail: durable de-dup marker for the overdue-follow-up email. Set when
    /// an overdue email is sent for the case's current overdue episode; while set
    /// (and the case is still overdue for the same reason) no further overdue
    /// email is sent — even across backend restarts or timer re-fires. Cleared
    /// when the episode ends (case resolved/closed, or a follow-up logged on/after
    /// the deadline). Nullable + nullable-column so it is safe to add to an
    /// existing SQLite DB via the Ensure*Column bootstrap helper.
    /// </summary>
    public DateTime? LastOverdueNotifiedUtc { get; set; }

    /// <summary>Navigation property: call / follow-up logs attached to this case.</summary>
    public ICollection<CallLog> CallLogs { get; set; } = new List<CallLog>();

    /// <summary>Navigation property: comments on the shared customer/staff thread.</summary>
    public ICollection<CaseComment> Comments { get; set; } = new List<CaseComment>();

    /// <summary>Navigation property: notifications associated with this case.</summary>
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
