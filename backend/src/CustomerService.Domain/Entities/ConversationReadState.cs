namespace CustomerService.Domain.Entities;

/// <summary>
/// Per-agent, per-case "last viewed" marker for the agent Messages tab. One
/// row exists per (AgentUserId, CaseId) pair. The unread indicator for a
/// conversation is derived by comparing the case's latest comment timestamp
/// against this agent's <see cref="LastViewedUtc"/> for that case — there is no
/// separate "unread" flag to keep in sync.
///
/// This is intentionally a separate, minimal table rather than overloading the
/// <see cref="Notification"/> table (which is for system-generated alerts such
/// as overdue/resolved/invite). Conversations and alerts are different
/// concerns with different lifecycles, so they are modelled separately.
/// </summary>
public class ConversationReadState
{
    /// <summary>Primary key (surrogate).</summary>
    public int Id { get; set; }

    /// <summary>The agent (User.Id) who owns this read marker.</summary>
    public string AgentUserId { get; set; } = string.Empty;

    /// <summary>The case this marker applies to.</summary>
    public int CaseId { get; set; }

    /// <summary>
    /// UTC timestamp of when the agent last viewed this conversation. A
    /// conversation is "unread" when its most recent comment is newer than
    /// this value.
    /// </summary>
    public DateTime LastViewedUtc { get; set; } = DateTime.UtcNow;
}
