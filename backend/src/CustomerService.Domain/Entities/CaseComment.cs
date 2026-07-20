namespace CustomerService.Domain.Entities;

/// <summary>
/// A comment on a case's shared thread. The thread is visible to BOTH the
/// owning customer (via the customer-portal routes) and to staff (via the
/// staff case routes) — it is the same underlying data, just reached through
/// two different authorization-scoped endpoints.
///
/// Exactly one of <see cref="AuthorUserId"/> / <see cref="AuthorCustomerId"/>
/// must be set per comment (never both, never neither). This invariant is
/// enforced in <c>CaseCommentService</c> when a comment is created, based on
/// which role's JWT is calling — it is not merely a convention.
/// </summary>
public class CaseComment
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the parent case.</summary>
    public int CaseId { get; set; }

    /// <summary>Navigation property to the parent case.</summary>
    public Case? Case { get; set; }

    /// <summary>
    /// Foreign key to the staff author (User), when the comment was posted by
    /// an agent/admin. Null when posted by the customer. Mutually exclusive
    /// with <see cref="AuthorCustomerId"/>.
    /// </summary>
    public string? AuthorUserId { get; set; }

    /// <summary>Navigation property to the staff author (when applicable).</summary>
    public User? AuthorUser { get; set; }

    /// <summary>
    /// Foreign key to the customer author, when the comment was posted by the
    /// customer themselves. Null when posted by staff. Mutually exclusive with
    /// <see cref="AuthorUserId"/>.
    /// </summary>
    public int? AuthorCustomerId { get; set; }

    /// <summary>Navigation property to the customer author (when applicable).</summary>
    public Customer? AuthorCustomer { get; set; }

    /// <summary>Comment body (required, non-empty, non-whitespace).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the comment was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
