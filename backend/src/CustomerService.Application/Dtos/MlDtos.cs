namespace CustomerService.Application.Dtos;

/// <summary>Features submitted to preview a priority prediction.</summary>
public class PredictPriorityRequest
{
    /// <summary>Category id of the case.</summary>
    public int CategoryId { get; set; }

    /// <summary>Number of cases the customer raised before this one.</summary>
    public int PriorCaseCount { get; set; }

    /// <summary>Days since the customer's last contact (before this case).</summary>
    public int DaysSinceLastContact { get; set; }

    /// <summary>True if the description contains a complaint/urgency keyword.</summary>
    public bool HasComplaintKeyword { get; set; }
}

/// <summary>Priority prediction preview returned by the ML endpoint.</summary>
public class PredictPriorityResponse
{
    /// <summary>The predicted priority.</summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>Plain-English reason for the suggestion.</summary>
    public string Reason { get; set; } = string.Empty;
}
