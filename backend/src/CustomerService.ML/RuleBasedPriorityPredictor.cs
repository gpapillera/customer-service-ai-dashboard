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
    /// <summary>Negative (urgency/complaint) words and their weights.</summary>
    private static readonly Dictionary<string, double> NegativeLexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        { "urgent", 2.0 }, { "asap", 2.0 }, { "immediately", 1.5 }, { "broken", 1.5 },
        { "error", 1.5 }, { "fail", 1.5 }, { "failed", 1.5 }, { "complaint", 2.0 },
        { "angry", 2.0 }, { "furious", 2.5 }, { "unacceptable", 2.0 }, { "refund", 1.0 },
        { "chargeback", 1.5 }, { "lawsuit", 2.5 }, { "escalate", 1.5 }, { "critical", 2.0 },
        { "down", 1.0 }, { "outage", 1.5 }, { "lost", 1.0 }, { "missing", 1.0 },
        { "terrible", 2.0 }, { "worst", 2.0 }, { "hate", 2.0 }, { "disappointed", 1.5 },
        { "frustrated", 1.5 }, { "useless", 1.5 }, { "scam", 2.0 }, { "ripoff", 2.0 },
        { "bug", 1.0 }, { "crash", 1.5 }, { "denied", 1.0 }, { "wrong", 0.8 },
        { "cancel", 0.5 }, { "problem", 0.5 }, { "issue", 0.3 }, { "slow", 0.5 },
        { "late", 0.5 }, { "never", 0.5 },
    };

    /// <summary>Positive (gratitude/satisfaction) words and their weights.</summary>
    private static readonly Dictionary<string, double> PositiveLexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        { "thank", 1.0 }, { "thanks", 1.0 }, { "appreciate", 1.5 }, { "happy", 1.5 },
        { "great", 1.0 }, { "excellent", 1.5 }, { "love", 1.5 }, { "resolved", 1.0 },
        { "solved", 1.0 }, { "fixed", 1.0 }, { "good", 0.8 }, { "perfect", 1.5 },
        { "satisfied", 1.5 }, { "wonderful", 1.5 }, { "amazing", 1.5 }, { "helpful", 1.0 },
        { "please", 0.3 }, { "kind", 1.0 }, { "quickly", 0.5 }, { "works", 0.5 },
        { "working", 0.5 }, { "glad", 1.0 }, { "pleased", 1.5 },
    };

    /// <inheritdoc/>
    public Priority Predict(PriorityFeatures features)
        => PredictWithReason(features).Priority;

    /// <inheritdoc/>
    public PriorityPredictionResult PredictWithReason(PriorityFeatures features)
    {
        var score = 0;
        var reasons = new List<string>();
        if (features.Sentiment < -0.1f)
        {
            score += 2;
            reasons.Add("the description expresses negative/complaint sentiment");
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

    /// <summary>Computes a sentiment score in [-1, 1] from the complaint/positive lexicons.</summary>
    /// <param name="text">Free-text description.</param>
    /// <returns>
    /// Negative for complaint/urgency language, positive for gratitude/satisfaction,
    /// 0 for neutral text. Mirrors the Python <c>sentiment_score</c> used for training.
    /// </returns>
    public static float SentimentScore(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;
        var low = text.ToLowerInvariant();
        double neg = NegativeLexicon.Sum(kv => low.Contains(kv.Key) ? kv.Value : 0);
        double pos = PositiveLexicon.Sum(kv => low.Contains(kv.Key) ? kv.Value : 0);
        double total = pos + neg;
        if (total == 0) return 0f;
        var score = (pos - neg) / total;
        return (float)Math.Max(-1.0, Math.Min(1.0, score));
    }
}
