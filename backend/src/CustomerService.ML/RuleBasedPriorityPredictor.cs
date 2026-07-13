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
        => PredictWithReason(features).Priority;

    /// <inheritdoc/>
    public PriorityPredictionResult PredictWithReason(PriorityFeatures features)
    {
        var score = 0;
        var reasons = new List<string>();
        if (features.HasComplaintKeyword)
        {
            score += 2;
            reasons.Add("the description contains urgent/complaint keywords");
        }
        if (features.DaysSinceLastContact > 30)
        {
            score += 1;
            reasons.Add($"the customer has had no contact for {features.DaysSinceLastContact} days");
        }
        if (features.PriorCaseCount >= 3)
        {
            score += 1;
            reasons.Add($"the customer has {features.PriorCaseCount} prior cases");
        }
        if (features.CategoryId == 1)
        {
            score += 1;
            reasons.Add("the category is Billing, which is often time-sensitive");
        }

        var priority = score >= 3 ? Priority.High : score >= 1 ? Priority.Medium : Priority.Low;
        var reason = reasons.Count == 0
            ? $"Routine {CategoryName(features.CategoryId).ToLowerInvariant()} case with no urgency signals — suggested {priority}."
            : $"Suggested {priority} because {string.Join(", and ", reasons)}.";
        return new PriorityPredictionResult { Priority = priority, Reason = reason };
    }

    private static string CategoryName(int categoryId) => categoryId switch
    {
        1 => "Billing",
        2 => "Shipping",
        3 => "Technical",
        4 => "Account",
        5 => "Product",
        _ => "General",
    };

    /// <summary>Detects complaint/urgency keywords in free text.</summary>
    /// <param name="text">Text to scan.</param>
    /// <returns>True if any keyword is present.</returns>
    public static bool ContainsComplaintKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ComplaintKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
