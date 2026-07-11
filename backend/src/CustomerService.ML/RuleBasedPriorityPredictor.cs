using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;

namespace CustomerService.ML;

/// <summary>
/// Deterministic, dependency-free fallback predictor used when no ONNX model
/// is available. Mirrors the rule-based labeling logic used to train the ML
/// model so the app is runnable end-to-end without Python/ONNX.
/// </summary>
public class RuleBasedPriorityPredictor : IPriorityPredictor
{
    private static readonly HashSet<string> ComplaintKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "urgent", "asap", "immediately", "broken", "error", "fail", "failed",
        "complaint", "angry", "furious", "unacceptable", "refund", "chargeback",
        "lawsuit", "escalate", "critical", "down", "outage", "lost", "missing",
    };

    /// <inheritdoc/>
    public Priority Predict(PriorityFeatures features)
    {
        var score = 0;
        if (features.HasComplaintKeyword) score += 2;
        if (features.DaysSinceLastContact > 30) score += 1;
        if (features.PriorCaseCount >= 3) score += 1;
        if (features.CategoryId == 1) score += 1; // Billing often urgent

        return score >= 3 ? Priority.High : score >= 1 ? Priority.Medium : Priority.Low;
    }

    /// <summary>Detects complaint/urgency keywords in free text.</summary>
    /// <param name="text">Text to scan.</param>
    /// <returns>True if any keyword is present.</returns>
    public static bool ContainsComplaintKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ComplaintKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
