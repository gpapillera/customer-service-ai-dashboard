using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.ML;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for the priority predictors. The rule-based predictor is
/// deterministic and dependency-free; the ONNX predictor is tested with a
/// null/absent model path so it transparently falls back to the rule-based
/// logic (the same path used when the .onnx file is missing in production).
/// </summary>
public class PredictorTests
{
    [Fact]
    public void RuleBased_ComplaintKeyword_AndBilling_EscalatesToHigh()
    {
        var predictor = new RuleBasedPriorityPredictor();
        var result = predictor.PredictWithReason(new PriorityFeatures
        {
            CategoryId = 1, // Billing
            PriorCaseCount = 0,
            DaysSinceLastContact = 0,
            HasComplaintKeyword = true, // +2
        });

        Assert.Equal(Priority.High, result.Priority);
        Assert.Contains("urgent/complaint keywords", result.Reason);
    }

    [Fact]
    public void RuleBased_NoSignals_IsLow()
    {
        var predictor = new RuleBasedPriorityPredictor();
        var result = predictor.PredictWithReason(new PriorityFeatures
        {
            CategoryId = 4, // Account
            PriorCaseCount = 0,
            DaysSinceLastContact = 1,
            HasComplaintKeyword = false,
        });

        Assert.Equal(Priority.Low, result.Priority);
    }

    [Fact]
    public void RuleBased_SingleSignal_IsMedium()
    {
        var predictor = new RuleBasedPriorityPredictor();
        var result = predictor.PredictWithReason(new PriorityFeatures
        {
            CategoryId = 3,
            PriorCaseCount = 0,
            DaysSinceLastContact = 0,
            HasComplaintKeyword = true, // +2 -> Medium (>=1, <3)
        });

        Assert.Equal(Priority.Medium, result.Priority);
    }

    [Fact]
    public void RuleBased_ContainsComplaintKeyword_DetectsKnownWords()
    {
        Assert.True(RuleBasedPriorityPredictor.ContainsComplaintKeyword("this is URGENT please help"));
        Assert.True(RuleBasedPriorityPredictor.ContainsComplaintKeyword("I want a refund"));
        Assert.False(RuleBasedPriorityPredictor.ContainsComplaintKeyword("just a general question"));
        Assert.False(RuleBasedPriorityPredictor.ContainsComplaintKeyword(null));
        Assert.False(RuleBasedPriorityPredictor.ContainsComplaintKeyword(""));
    }

    [Fact]
    public void Onnx_WithoutModelFile_FallsBackToRuleBased()
    {
        // No model at this path -> constructor leaves the session null and the
        // predictor uses the deterministic rule-based logic.
        var predictor = new OnnxPriorityPredictor("nonexistent-model.onnx");

        var result = predictor.PredictWithReason(new PriorityFeatures
        {
            CategoryId = 1,
            PriorCaseCount = 0,
            DaysSinceLastContact = 0,
            HasComplaintKeyword = true,
        });

        Assert.Equal(Priority.High, result.Priority);
    }

    [Fact]
    public void Onnx_NullPath_FallsBackToRuleBased()
    {
        var predictor = new OnnxPriorityPredictor(null);
        Assert.Equal(Priority.Low, predictor.Predict(new PriorityFeatures
        {
            CategoryId = 4,
            PriorCaseCount = 0,
            DaysSinceLastContact = 1,
            HasComplaintKeyword = false,
        }));
    }
}
