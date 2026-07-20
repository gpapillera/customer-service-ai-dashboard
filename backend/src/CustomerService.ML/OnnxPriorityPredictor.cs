using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CustomerService.ML;

/// <summary>
/// ONNX-based priority predictor. Loads a model exported from the Python
/// training pipeline (ml/train_model.py -> ml/models/priority_model.onnx).
/// Falls back to <see cref="RuleBasedPriorityPredictor"/> when the model file
/// is absent so the API always runs.
/// See docs/DIY.md §9 (backend wiring) and §10 (Python pipeline that builds this model).
/// </summary>
public class OnnxPriorityPredictor : IPriorityPredictor, IDisposable
{
    private readonly InferenceSession? _session;
    private readonly RuleBasedPriorityPredictor _fallback = new();
    private readonly string[] _labels = { "Low", "Medium", "High" };

    /// <summary>
    /// Initializes a new <see cref="OnnxPriorityPredictor"/>.
    /// </summary>
    /// <param name="modelPath">
    /// Path to the ONNX model. When null/missing, the rule-based fallback is used.
    /// </param>
    public OnnxPriorityPredictor(string? modelPath = null)
    {
        if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
        {
            _session = new InferenceSession(modelPath);
        }
    }

    /// <inheritdoc/>
    public Priority Predict(PriorityFeatures features)
        => PredictWithReason(features).Priority;

    /// <inheritdoc/>
    public PriorityPredictionResult PredictWithReason(PriorityFeatures features)
    {
        if (_session is null)
        {
            return _fallback.PredictWithReason(features);
        }

        // Feature vector order must match the Python training pipeline:
        // [categoryId, priorCaseCount, daysSinceLastContact, sentiment]
        var input = new float[]
        {
            features.CategoryId,
            features.PriorCaseCount,
            features.DaysSinceLastContact,
            features.Sentiment,
        };
        var tensor = new DenseTensor<float>(input, new[] { 1, 4 });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", tensor),
        };

        return PredictWithReasonFromOutput(features, inputs);
    }

    /// <summary>Runs the session and builds a prediction result from the output.</summary>
    private PriorityPredictionResult PredictWithReasonFromOutput(PriorityFeatures features, List<NamedOnnxValue> inputs)
    {
        if (_session is null) return _fallback.PredictWithReason(features);
        using var results = _session.Run(inputs);
        // The model emits two outputs: "label" (string) and "probabilities"
        // (float[3] in [Low, Medium, High] order). Prefer the probabilities
        // output by name, falling back to the first output for safety.
        var prob = results.FirstOrDefault(r => r.Name == "probabilities") ?? results.First();
        var output = prob.AsEnumerable<float>().ToArray();
        var predicted = Array.IndexOf(output, output.Max());
        var priority = Enum.Parse<Priority>(_labels[predicted]);
        return new PriorityPredictionResult
        {
            Priority = priority,
            Reason = BuildReason(features, priority, output),
            Source = PriorityModelSource.Onnx,
        };
    }

    /// <summary>Builds a plain-English reason from the features and model output.</summary>
    private string BuildReason(PriorityFeatures features, Priority priority, float[] probs)
    {
        var reasons = new List<string>();
        if (features.Sentiment < -0.1f)
            reasons.Add("the description expresses negative/complaint sentiment");
        if (features.DaysSinceLastContact > 30)
            reasons.Add($"the customer has had no contact for {features.DaysSinceLastContact} days");
        if (features.PriorCaseCount >= 3)
            reasons.Add($"the customer has {features.PriorCaseCount} prior cases");
        if (features.CategoryId == 1)
            reasons.Add("the category is Billing, which is often time-sensitive");

        var confidence = probs.Length == 3 ? probs[Array.IndexOf(_labels, priority.ToString())] : 0f;
        var tail = reasons.Count == 0
            ? $"no urgency signals were detected (model confidence {confidence:P0})"
            : string.Join(", and ", reasons);
        return $"Suggested {priority} because {tail}.";
    }

    /// <inheritdoc/>
    public void Dispose() => _session?.Dispose();
}
