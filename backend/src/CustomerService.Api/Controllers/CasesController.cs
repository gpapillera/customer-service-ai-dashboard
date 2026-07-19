using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// CRUD + filtering endpoints for cases. POST auto-suggests priority via ML.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CasesController : ControllerBase
{
    private readonly ICaseService _service;

    /// <summary>Initializes a new <see cref="CasesController"/>.</summary>
    /// <param name="service">Case service.</param>
    public CasesController(ICaseService service) => _service = service;

    /// <summary>Lists cases with optional filters.</summary>
    /// <param name="status">Status filter.</param>
    /// <param name="priority">Priority filter.</param>
    /// <param name="categoryId">Category filter.</param>
    /// <param name="from">Created-from date (UTC).</param>
    /// <param name="to">Created-to date (UTC).</param>
    /// <param name="overdue">When true, only open cases with a past follow-up deadline and no follow-up since.</param>
    /// <returns>Matching cases.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CaseDto>> GetAll(
        [FromQuery] CaseStatus? status,
        [FromQuery] Priority? priority,
        [FromQuery] int? categoryId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] bool overdue = false)
        => await _service.GetAllAsync(status, priority, categoryId, from, to, overdue);

    /// <summary>Gets a case by id.</summary>
    /// <param name="id">Case id.</param>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CaseDto>> GetById(int id)
        => await _service.GetByIdAsync(id) is { } c ? Ok(c) : NotFound();

    /// <summary>Creates a case. Priority is ML-suggested when not supplied.</summary>
    /// <param name="dto">Create payload.</param>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CaseDto>> Create([FromBody] CreateCaseDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Updates a case (priority override allowed).</summary>
    /// <param name="id">Case id.</param>
    /// <param name="dto">Update payload.</param>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCaseDto dto)
    {
        await _service.UpdateAsync(id, dto);
        return NoContent();
    }

    /// <summary>Deletes a case.</summary>
    /// <param name="id">Case id.</param>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}
