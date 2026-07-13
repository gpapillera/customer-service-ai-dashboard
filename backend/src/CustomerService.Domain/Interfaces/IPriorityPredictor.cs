using CustomerService.Domain.Entities;

namespace CustomerService.Domain.Interfaces;

/// <summary>
/// Features passed to the priority model when scoring a new case.
/// </summary>
public class PriorityFeatures
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

/// <summary>
/// A priority prediction together with a short, human-readable explanation
/// of why that level was chosen (built from the input features).
/// </summary>
public class PriorityPredictionResult
{
    /// <summary>The predicted priority.</summary>
    public Priority Priority { get; init; }

    /// <summary>Plain-English reason for the suggestion (1–2 sentences).</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Contract for the ML priority predictor. Implemented in CustomerService.ML
/// (ONNX) or as a deterministic fallback when no model is present.
/// </summary>
public interface IPriorityPredictor
{
    /// <summary>Predicts a priority for the given features.</summary>
    /// <param name="features">Input features.</param>
    /// <returns>The predicted <see cref="Priority"/>.</returns>
    Priority Predict(PriorityFeatures features);

    /// <summary>Predicts a priority and returns a plain-English reason.</summary>
    /// <param name="features">Input features.</param>
    /// <returns>A <see cref="PriorityPredictionResult"/> with priority + reason.</returns>
    PriorityPredictionResult PredictWithReason(PriorityFeatures features);
}
