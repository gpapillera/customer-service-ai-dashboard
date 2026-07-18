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

    /// <summary>Case description; the backend derives a sentiment score from it.</summary>
    public string? Description { get; set; }
}

/// <summary>Priority prediction preview returned by the ML endpoint.</summary>
public class PredictPriorityResponse
{
    /// <summary>The predicted priority.</summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>Plain-English reason for the suggestion.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Which engine produced the prediction: "Onnx" for the trained ML model,
    /// or "RuleBased" for the deterministic fallback (used when the ONNX model
    /// is absent). Lets the UI show whether a real model suggestion was used.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
