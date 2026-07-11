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
/// Contract for the ML priority predictor. Implemented in CustomerService.ML
/// (ONNX) or as a deterministic fallback when no model is present.
/// </summary>
public interface IPriorityPredictor
{
    /// <summary>Predicts a priority for the given features.</summary>
    /// <param name="features">Input features.</param>
    /// <returns>The predicted <see cref="Priority"/>.</returns>
    Priority Predict(PriorityFeatures features);
}
