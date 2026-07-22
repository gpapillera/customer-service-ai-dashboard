using System.ComponentModel.DataAnnotations;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>
/// Customer-facing case list item. Deliberately a SEPARATE shape from the
/// staff <see cref="CaseDto"/> — it never carries priority, AI reasoning,
/// assigned-agent, or category (category is treated as internal-only here).
/// </summary>
public class CustomerCaseSummaryDto
{
    /// <summary>Case primary key.</summary>
    public int Id { get; set; }

    /// <summary>Subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Lifecycle status.</summary>
    public CaseStatus Status { get; set; }

    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Timestamp of the most recent staff comment (UTC), or null if no staff comments exist.</summary>
    public DateTime? LastStaffCommentAtUtc { get; set; }

    /// <summary>Total number of comments in the thread.</summary>
    public int CommentCount { get; set; }
}

/// <summary>
/// Customer-facing case detail. Excludes the entire AI Priority Prediction
/// section (suggested/final priority + reasoning), the call log, and the
/// assigned-agent identity. <see cref="ResolvedAtUtc"/> is included (read-only)
/// so the customer can see when their issue was resolved.
/// </summary>
public class CustomerCaseDetailDto
{
    /// <summary>Case primary key.</summary>
    public int Id { get; set; }

    /// <summary>Subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Lifecycle status.</summary>
    public CaseStatus Status { get; set; }

    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Resolved timestamp (UTC), or null while open.</summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>The shared comment thread (customer + staff).</summary>
    public IReadOnlyList<CaseCommentDto> Comments { get; set; } = Array.Empty<CaseCommentDto>();
}

/// <summary>
/// A single comment on the shared thread. <see cref="IsStaff"/> lets the
/// frontend style staff vs customer comments differently.
/// </summary>
public class CaseCommentDto
{
    /// <summary>Comment primary key.</summary>
    public int Id { get; set; }

    /// <summary>Display name of the author (customer name or staff full name).</summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>True when the author was staff (agent/admin), false when the customer themselves.</summary>
    public bool IsStaff { get; set; }

    /// <summary>Comment body.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Body for posting a comment (customer or staff).</summary>
public class CreateCaseCommentDto
{
    /// <summary>Comment body (required, non-empty, non-whitespace).</summary>
    [Required(ErrorMessage = "Comment body is required.")]
    [StringLength(4000, ErrorMessage = "Comment must be 4000 characters or fewer.")]
    public string Body { get; set; } = string.Empty;
}

/// <summary>
/// Body for a customer-created case. The customer id is NEVER part of this
/// payload — it is taken from the JWT claim by the controller. Only the fields
/// a customer is allowed to supply are present.
/// </summary>
public class CreateCustomerCaseDto
{
    /// <summary>Case subject.</summary>
    [Required(ErrorMessage = "Subject is required.")]
    [StringLength(300, ErrorMessage = "Subject must be 300 characters or fewer.")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>Case description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Category id (must reference a seeded category).</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A valid category is required.")]
    public int CategoryId { get; set; }
}
