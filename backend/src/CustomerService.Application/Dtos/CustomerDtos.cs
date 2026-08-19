using System.ComponentModel.DataAnnotations;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>Data transfer object for creating a customer.</summary>
public class CreateCustomerDto
{
    /// <summary>Customer full name.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Email address.</summary>
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email is required.")]
    [StringLength(200, ErrorMessage = "Email must be 200 characters or fewer.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional phone number.</summary>
    [StringLength(30, ErrorMessage = "Phone must be 30 characters or fewer.")]
    public string? Phone { get; set; }

    /// <summary>Optional company name.</summary>
    [StringLength(150, ErrorMessage = "Company must be 150 characters or fewer.")]
    public string? Company { get; set; }

    /// <summary>Optional address.</summary>
    public string? Address { get; set; }
}

/// <summary>Data transfer object for updating a customer.</summary>
public class UpdateCustomerDto : CreateCustomerDto
{
    /// <summary>Customer primary key.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A valid id is required.")]
    public int Id { get; set; }
}

/// <summary>Read model for a customer (includes case count and account status).</summary>
public class CustomerDto
{
    /// <summary>Customer primary key.</summary>
    public int Id { get; set; }

    /// <summary>Human-readable display ID (e.g. "CUST-00001").</summary>
    public string? CustomerDisplayId { get; set; }

    /// <summary>Customer full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Company name.</summary>
    public string? Company { get; set; }

    /// <summary>Address.</summary>
    public string? Address { get; set; }

    /// <summary>Number of cases raised by this customer.</summary>
    public int CaseCount { get; set; }

    /// <summary>Number of active (non-resolved, non-closed) cases.</summary>
    public int ActiveCaseCount { get; set; }

    /// <summary>Active cases with subject and status (for hover tooltip).</summary>
    public List<ActiveCaseInfoDto> ActiveCases { get; set; } = new();

    /// <summary>UTC timestamp when the customer record was created.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp of the last account-level profile edit, or null if never edited.</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>UTC timestamp of the most recent activity across all customer cases.</summary>
    public DateTime? LastActivityAtUtc { get; set; }

    /// <summary>Human-readable description of the most recent activity (e.g. "Messaged customer").</summary>
    public string? LastActivityDescription { get; set; }

    /// <summary>
    /// Id of the case that produced the most recent activity, when any exists.
    /// Lets the UI deep-link from the customer card's activity footer to the
    /// exact case's history (customers may have one or many cases).
    /// </summary>
    public int? LastActivityCaseId { get; set; }

    /// <summary>True if the customer has an account record (login credentials).</summary>
    public bool HasAccount { get; set; }

    /// <summary>True if the customer's account is active (password set).</summary>
    public bool AccountActive { get; set; }

    /// <summary>
    /// True when the customer has been soft-deleted and is sitting in the
    /// recycle bin. Only populated by the recycle-bin endpoint; normal reads
    /// never return soft-deleted rows.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp the customer was soft-deleted (recycle bin), or null.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>Id of the user who soft-deleted the customer, or null.</summary>
    public string? DeletedById { get; set; }

    /// <summary>
    /// True once the customer has been purged (PII anonymized). A purged row
    /// is excluded from the recycle bin and is not restorable.
    /// </summary>
    public bool Purged { get; set; }
}

/// <summary>Minimal info for an active case (subject + status).</summary>
public class ActiveCaseInfoDto
{
    /// <summary>Case subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Case status (serialized as string via JsonStringEnumConverter).</summary>
    public CaseStatus Status { get; set; }
}
