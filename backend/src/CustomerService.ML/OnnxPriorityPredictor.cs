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
    {
        if (_session is null)
        {
            return _fallback.Predict(features);
        }

        // Feature vector order must match the Python training pipeline:
        // [categoryId, priorCaseCount, daysSinceLastContact, hasComplaintKeyword]
        var input = new float[]
        {
            features.CategoryId,
            features.PriorCaseCount,
            features.DaysSinceLastContact,
            features.HasComplaintKeyword ? 1f : 0f,
        };
        var tensor = new DenseTensor<float>(input, new[] { 1, 4 });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", tensor),
        };

        using var results = _session.Run(inputs);
        // The model emits two outputs: "label" (string) and "probabilities"
        // (float[3] in [Low, Medium, High] order). Prefer the probabilities
        // output by name, falling back to the first output for safety.
        var prob = results.FirstOrDefault(r => r.Name == "probabilities") ?? results.First();
        var output = prob.AsEnumerable<float>().ToArray();
        var predicted = Array.IndexOf(output, output.Max());
        return Enum.Parse<Priority>(_labels[predicted]);
    }

    /// <inheritdoc/>
    public void Dispose() => _session?.Dispose();
}
