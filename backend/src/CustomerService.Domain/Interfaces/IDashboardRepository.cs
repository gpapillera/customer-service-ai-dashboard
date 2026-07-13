using CustomerService.Domain.Entities;

namespace CustomerService.Domain.Interfaces;

/// <summary>
/// Application-level contract for dashboard analytics. Implemented in Infrastructure.
/// </summary>
public interface IDashboardRepository
{
    /// <summary>Computes KPI totals and status/priority breakdowns.</summary>
    /// <returns>A <see cref="DashboardSummary"/> with aggregate metrics.</returns>
    Task<DashboardSummary> GetSummaryAsync();

    /// <summary>Returns cases created per day for the last <paramref name="days"/> days.</summary>
    /// <param name="days">Number of trailing days to aggregate.</param>
    /// <returns>A list of (date, count) pairs ordered by date.</returns>
    Task<IReadOnlyList<DateCount>> GetCasesCreatedTrendAsync(int days);

    /// <summary>Returns case counts grouped by category.</summary>
    /// <returns>A list of (categoryName, count) pairs.</returns>
    Task<IReadOnlyList<CategoryCount>> GetCasesByCategoryAsync();

    /// <summary>Returns the most recent cases (for the dashboard list).</summary>
    /// <param name="limit">Maximum number of cases to return.</param>
    /// <returns>A list of recent <see cref="Case"/> entities.</returns>
    Task<IReadOnlyList<Case>> GetRecentCasesAsync(int limit);
}

/// <summary>Lightweight aggregate returned by <see cref="IDashboardRepository"/>.</summary>
public class DashboardSummary
{
    /// <summary>Total number of cases.</summary>
    public int TotalCases { get; set; }

    /// <summary>Number of open (New/InProgress/Escalated) cases.</summary>
    public int OpenCases { get; set; }

    /// <summary>Number of closed cases.</summary>
    public int ClosedCases { get; set; }

    /// <summary>Number of high-priority cases.</summary>
    public int HighPriorityCases { get; set; }

    /// <summary>Number of resolved cases.</summary>
    public int ResolvedCases { get; set; }

    /// <summary>Number of cases whose priority was ML-suggested.</summary>
    public int AiPredictedCases { get; set; }

    /// <summary>Total number of customers.</summary>
    public int TotalCustomers { get; set; }

    /// <summary>Count of cases per status.</summary>
    public Dictionary<string, int> ByStatus { get; set; } = new();

    /// <summary>Count of cases per priority.</summary>
    public Dictionary<string, int> ByPriority { get; set; } = new();
}

/// <summary>A date/count pair used in trend charts.</summary>
public class DateCount
{
    /// <summary>The date (day granularity).</summary>
    public DateTime Date { get; set; }

    /// <summary>The count for that date.</summary>
    public int Count { get; set; }
}

/// <summary>A category/count pair used in breakdown charts.</summary>
public class CategoryCount
{
    /// <summary>The category name.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The count for that category.</summary>
    public int Count { get; set; }
}
