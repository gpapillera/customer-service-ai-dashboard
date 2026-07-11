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

    /// <summary>Total customers.</summary>
    public int TotalCustomers { get; set; }

    /// <summary>Cases per status (label -> count).</summary>
    public Dictionary<string, int> ByStatus { get; set; } = new();

    /// <summary>Cases per priority (label -> count).</summary>
    public Dictionary<string, int> ByPriority { get; set; } = new();

    /// <summary>Daily case-creation trend (last 30 days).</summary>
    public List<DateCountDto> Trend { get; set; } = new();

    /// <summary>Case counts per category.</summary>
    public List<CategoryCountDto> ByCategory { get; set; } = new();
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
