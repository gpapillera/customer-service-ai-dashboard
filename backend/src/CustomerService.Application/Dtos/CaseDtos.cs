using System.ComponentModel.DataAnnotations;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>Data transfer object for creating a case.</summary>
public class CreateCaseDto
{
    /// <summary>Case subject.</summary>
    [Required(ErrorMessage = "Subject is required.")]
    [StringLength(300, ErrorMessage = "Subject must be 300 characters or fewer.")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>Case description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Category id.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A valid category is required.")]
    public int CategoryId { get; set; }

    /// <summary>Customer id.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A valid customer is required.")]
    public int CustomerId { get; set; }

    /// <summary>Optional assigned agent id.</summary>
    public string? AssignedToUserId { get; set; }

    /// <summary>Optional explicit priority; if omitted the ML model suggests one.</summary>
    public Priority? Priority { get; set; }

    /// <summary>UTC timestamp of customer's last contact before this case (ML feature).</summary>
    public DateTime? LastContactUtc { get; set; }
}

/// <summary>Data transfer object for updating a case.</summary>
public class UpdateCaseDto
{
    /// <summary>Case subject.</summary>
    [Required(ErrorMessage = "Subject is required.")]
    [StringLength(300, ErrorMessage = "Subject must be 300 characters or fewer.")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>Case description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Lifecycle status.</summary>
    [Required(ErrorMessage = "Status is required.")]
    public CaseStatus Status { get; set; }

    /// <summary>Priority (may be manually overridden).</summary>
    [Required(ErrorMessage = "Priority is required.")]
    public Priority Priority { get; set; }

    /// <summary>Category id.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A valid category is required.")]
    public int CategoryId { get; set; }

    /// <summary>Assigned agent id.</summary>
    /// <remarks>
    /// A <c>null</c> value is treated as "omit" — the existing assignee is
    /// preserved (the UI always sends null for fields it does not edit). To
    /// explicitly UNASSIGN a case, send the sentinel
    /// <see cref="UnassignSentinel"/> (<c>"__unassign__"</c>); the service
    /// then clears <c>AssignedToUserId</c>.
    /// </remarks>
    public string? AssignedToUserId { get; set; }

    /// <summary>Sentinel value for <see cref="AssignedToUserId"/> that means "explicitly unassign this case".</summary>
    public const string UnassignSentinel = "__unassign__";
}

/// <summary>Read model for a case (with related names).</summary>
public class CaseDto
{
    /// <summary>Case primary key.</summary>
    public int Id { get; set; }

    /// <summary>Subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Status.</summary>
    public CaseStatus Status { get; set; }

    /// <summary>Priority.</summary>
    public Priority Priority { get; set; }

    /// <summary>True if priority was ML-suggested.</summary>
    public bool PriorityAutoSuggested { get; set; }

    /// <summary>Plain-English reason for the ML-suggested priority (when auto-suggested).</summary>
    public string? PriorityReason { get; set; }

    /// <summary>Customer id.</summary>
    public int CustomerId { get; set; }

    /// <summary>Customer name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Category id.</summary>
    public int CategoryId { get; set; }

    /// <summary>Category name.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Assigned agent id.</summary>
    public string? AssignedToUserId { get; set; }

    /// <summary>Assigned agent name.</summary>
    public string? AssignedToUserName { get; set; }

    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Last updated timestamp (UTC).</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>UTC deadline for the next follow-up (null = none scheduled).</summary>
    public DateTime? FollowUpDueUtc { get; set; }

    /// <summary>
    /// Days past the follow-up reference point (rounded up, minimum 1) when the
    /// case is overdue; null otherwise. Computed server-side via
    /// <see cref="OverduePolicy.DaysOverdue"/> so the cases endpoint and the
    /// dashboard always agree (and avoid timezone drift from client-side math).
    /// </summary>
    public int? DaysOverdue { get; set; }
}

/// <summary>
/// A single entry in an agent's "Messages" (conversations) list. Summarises a
/// case assigned to the agent that has at least one comment, surfacing the most
/// recent comment and whether the agent has an unread message.
/// </summary>
public class ConversationSummaryDto
{
    /// <summary>Case id.</summary>
    public int CaseId { get; set; }

    /// <summary>Case subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Customer display name (case owner).</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Id of the most recent comment in the conversation.</summary>
    public int LastCommentId { get; set; }

    /// <summary>Truncated body of the most recent comment.</summary>
    public string LastCommentSnippet { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the most recent comment.</summary>
    public DateTime LastCommentAtUtc { get; set; }

    /// <summary>Display name of the author of the most recent comment.</summary>
    public string LastCommentAuthor { get; set; } = string.Empty;

    /// <summary>
    /// True when the latest comment is newer than the agent's last-viewed
    /// marker for this case (or the agent has never viewed it).
    /// </summary>
    public bool Unread { get; set; }

    /// <summary>
    /// Total number of unread comments (messages) in this conversation —
    /// i.e. non-self comments created after the agent's last-viewed marker.
    /// Used by the nav badge to show an aggregate count across all conversations.
    /// </summary>
    public int UnreadCount { get; set; }

    /// <summary>
    /// Display name of the agent assigned to the case, or null when
    /// the case is unassigned.
    /// </summary>
    public string? AssignedAgentName { get; set; }
}
