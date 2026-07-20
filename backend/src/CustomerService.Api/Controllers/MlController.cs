using CustomerService.Application.Dtos;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.ML;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Exposes the priority predictor so the frontend can preview an AI suggestion
/// on demand (e.g. before saving a case) without creating the case.
/// </summary>
[ApiController]
[Route("api/ml")]
[Authorize(Roles = "Admin,Agent")]
public class MlController : ControllerBase
{
    private readonly IPriorityPredictor _predictor;

    /// <summary>Initializes a new <see cref="MlController"/>.</summary>
    /// <param name="predictor">Priority predictor (ML or rule-based fallback).</param>
    public MlController(IPriorityPredictor predictor) => _predictor = predictor;

    /// <summary>Previews a priority prediction for the supplied features.</summary>
    /// <param name="request">Prediction features.</param>
    /// <returns>A <see cref="PredictPriorityResponse"/> with priority + reason.</returns>
    [HttpPost("predict-priority")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PredictPriorityResponse> PredictPriority([FromBody] PredictPriorityRequest request)
    {
        var sentiment = RuleBasedPriorityPredictor.SentimentScore(request.Description);
        var result = _predictor.PredictWithReason(new PriorityFeatures
        {
            CategoryId = request.CategoryId,
            PriorCaseCount = request.PriorCaseCount,
            DaysSinceLastContact = request.DaysSinceLastContact,
            Sentiment = sentiment,
        });
        return Ok(new PredictPriorityResponse
        {
            Priority = result.Priority.ToString(),
            Reason = result.Reason,
            Source = result.Source.ToString(),
        });
    }
}
