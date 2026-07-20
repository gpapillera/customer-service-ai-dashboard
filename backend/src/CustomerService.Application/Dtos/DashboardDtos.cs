using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>Aggregated dashboard payload returned by GET /api/dashboard.</summary>
public class DashboardDto
{
    /// <summary>Total cases.</summary>
    public int TotalCases { get; set; }

    /// <summary>Open cases.</summary>
    public int OpenCases { get; set; }

    /// <summary>Closed cases.</summary>
    public int ClosedCases { get; set; }

    /// <summary>High-priority cases.</summary>
    public int HighPriorityCases { get; set; }

    /// <summary>Resolved cases (status == Resolved).</summary>
    public int ResolvedCases { get; set; }

    /// <summary>Cases whose priority was ML-suggested.</summary>
    public int AiPredictedCases { get; set; }

    /// <summary>Total customers.</summary>
    public int TotalCustomers { get; set; }

    // ---- Agent-scoped ("My *") totals. Populated only for an Agent caller;
    // left at zero for the company-wide Admin view. ----

    /// <summary>Cases assigned to the calling agent.</summary>
    public int MyCases { get; set; }

    /// <summary>Assigned cases not yet Resolved/Closed.</summary>
    public int MyOpenCases { get; set; }

    /// <summary>Assigned high-priority cases.</summary>
    public int MyHighPriorityCases { get; set; }

    /// <summary>Assigned cases whose priority was ML-suggested.</summary>
    public int MyAiPredictedCases { get; set; }

    /// <summary>Assigned cases with status == Resolved.</summary>
    public int MyResolvedCases { get; set; }

    /// <summary>Assigned open cases whose follow-up is overdue.</summary>
    public int MyOverdueFollowUps { get; set; }

    /// <summary>Most recent cases (for the "Recent Cases" list).</summary>
    public List<RecentCaseDto> RecentCases { get; set; } = new();

    /// <summary>Cases per status (label -> count).</summary>
    public Dictionary<string, int> ByStatus { get; set; } = new();

    /// <summary>Cases per priority (label -> count).</summary>
    public Dictionary<string, int> ByPriority { get; set; } = new();

    /// <summary>Daily case-creation trend (last 30 days).</summary>
    public List<DateCountDto> Trend { get; set; } = new();

    /// <summary>Case counts per category.</summary>
    public List<CategoryCountDto> ByCategory { get; set; } = new();

    /// <summary>Number of open cases whose scheduled follow-up is overdue.</summary>
    public int OverdueFollowUps { get; set; }

    /// <summary>Details of the overdue follow-ups (for the dashboard list).</summary>
    public List<OverdueFollowUpDto> OverdueFollowUpsList { get; set; } = new();
}

/// <summary>Summary of a case with an overdue follow-up (dashboard list item).</summary>
public class OverdueFollowUpDto
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

    /// <summary>The follow-up deadline that has passed (UTC, ISO 8601).</summary>
    public DateTime FollowUpDueUtc { get; set; }

    /// <summary>Number of days past the deadline (minimum 1).</summary>
    public int DaysOverdue { get; set; }
}

/// <summary>Date/count pair for trend charts.</summary>
public class DateCountDto
{
    /// <summary>ISO date (yyyy-mm-dd).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Count.</summary>
    public int Count { get; set; }
}

/// <summary>Category/count pair for breakdown charts.</summary>
public class CategoryCountDto
{
    /// <summary>Category name.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Count.</summary>
    public int Count { get; set; }
}

/// <summary>A recent case summary for the dashboard "Recent Cases" list.</summary>
public class RecentCaseDto
{
    /// <summary>Case id.</summary>
    public int Id { get; set; }

    /// <summary>Subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Customer name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Category name.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Priority.</summary>
    public Priority Priority { get; set; }

    /// <summary>Status.</summary>
    public CaseStatus Status { get; set; }

    /// <summary>True if priority was ML-suggested.</summary>
    public bool PriorityAutoSuggested { get; set; }
}
