using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>Data transfer object for creating a case.</summary>
public class CreateCaseDto
{
    /// <summary>Case subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Case description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Category id.</summary>
    public int CategoryId { get; set; }

    /// <summary>Customer id.</summary>
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
    public string Subject { get; set; } = string.Empty;

    /// <summary>Case description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Lifecycle status.</summary>
    public CaseStatus Status { get; set; }

    /// <summary>Priority (may be manually overridden).</summary>
    public Priority Priority { get; set; }

    /// <summary>Category id.</summary>
    public int CategoryId { get; set; }

    /// <summary>Assigned agent id.</summary>
    public string? AssignedToUserId { get; set; }
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
}
