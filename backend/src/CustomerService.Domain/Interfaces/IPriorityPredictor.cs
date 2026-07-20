using CustomerService.Domain.Entities;

namespace CustomerService.Domain.Interfaces;

/// <summary>
/// Features passed to the priority model when scoring a new case.
/// See docs/DIY.md §9 (backend ML wiring) and §10 (Python training pipeline).
/// </summary>
public class PriorityFeatures
{
    /// <summary>Category id of the case.</summary>
    public int CategoryId { get; set; }

    /// <summary>Number of cases the customer raised before this one.</summary>
    public int PriorCaseCount { get; set; }

    /// <summary>Days since the customer's last contact (before this case).</summary>
    public int DaysSinceLastContact { get; set; }

    /// <summary>Sentiment score of the description in [-1, 1]; negative = complaint/urgency, positive = satisfaction.</summary>
    public float Sentiment { get; set; }
}

/// <summary>
/// Identifies which engine produced a <see cref="PriorityPredictionResult"/>.
/// </summary>
public enum PriorityModelSource
{
    /// <summary>The ONNX model trained by the Python pipeline (ml/train_model.py).</summary>
    Onnx,

    /// <summary>The deterministic, dependency-free rule-based fallback used when no ONNX model is present.</summary>
    RuleBased,
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

    /// <summary>
    /// Which engine produced this prediction. Lets callers distinguish a real
    /// ML suggestion from the rule-based fallback (e.g. when the ONNX model is
    /// absent) so the fallback is never silent.
    /// </summary>
    public PriorityModelSource Source { get; init; } = PriorityModelSource.RuleBased;
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
