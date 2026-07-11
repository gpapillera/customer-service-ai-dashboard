namespace CustomerService.Domain.Entities;

/// <summary>
/// Lifecycle status of a support case.
/// </summary>
public enum CaseStatus
{
    /// <summary>Newly created, not yet worked.</summary>
    New = 0,

    /// <summary>Agent is actively working the case.</summary>
    InProgress = 1,

    /// <summary>Waiting on customer or third party.</summary>
    OnHold = 2,

    /// <summary>Resolved and closed.</summary>
    Closed = 3,
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

    /// <summary>UTC timestamp when the case was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last status change.</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>UTC timestamp of the customer's last contact before this case (for ML feature).</summary>
    public DateTime? LastContactUtc { get; set; }

    /// <summary>Navigation property: call / follow-up logs attached to this case.</summary>
    public ICollection<CallLog> CallLogs { get; set; } = new List<CallLog>();
}
