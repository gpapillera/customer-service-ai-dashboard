using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Endpoints for call / follow-up logs attached to cases.
/// See docs/DIY.md §7 for the call-log + notification walkthrough.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CallLogsController : ControllerBase
{
    private readonly ICallLogService _service;

    /// <summary>Initializes a new <see cref="CallLogsController"/>.</summary>
    /// <param name="service">Call log service.</param>
    public CallLogsController(ICallLogService service) => _service = service;

    /// <summary>Lists call logs for a case.</summary>
    /// <param name="caseId">Parent case id.</param>
    [HttpGet("case/{caseId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CallLogDto>> GetByCase(int caseId)
        => await _service.GetByCaseAsync(caseId);

    /// <summary>Adds a call log to a case.</summary>
    /// <param name="dto">Create payload.</param>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallLogDto>> Create([FromBody] CreateCallLogDto dto)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var created = await _service.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetByCase), new { caseId = created.CaseId }, created);
    }
}
