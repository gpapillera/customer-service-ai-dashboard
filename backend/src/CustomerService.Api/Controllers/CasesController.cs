using System.Security.Claims;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// CRUD + filtering endpoints for cases. POST auto-suggests priority via ML.
/// Staff-only (Admin/Agent) — a Customer-role token is rejected. See docs/DIY.md
/// §6 (case UI + filter toolbar) and §9 (ML priority).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class CasesController : ControllerBase
{
    private readonly ICaseService _service;
    private readonly ICaseCommentService _comments;

    /// <summary>Initializes a new <see cref="CasesController"/>.</summary>
    /// <param name="service">Case service.</param>
    /// <param name="comments">Shared case-comment service.</param>
    public CasesController(ICaseService service, ICaseCommentService comments)
    {
        _service = service;
        _comments = comments;
    }

    /// <summary>Lists cases with optional filters.</summary>
    /// <param name="status">Status filter.</param>
    /// <param name="priority">Priority filter.</param>
    /// <param name="categoryId">Category filter.</param>
    /// <param name="from">Created-from date (UTC).</param>
    /// <param name="to">Created-to date (UTC).</param>
    /// <param name="overdue">When true, only open cases with a past follow-up deadline and no follow-up since.</param>
    /// <param name="assignedToMe">When true, only cases assigned to the calling user (id from the JWT, never the client).</param>
    /// <returns>Matching cases.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CaseDto>> GetAll(
        [FromQuery] CaseStatus? status,
        [FromQuery] Priority? priority,
        [FromQuery] int? categoryId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] bool overdue = false,
        [FromQuery] bool assignedToMe = false)
    {
        // "Assigned to me" is resolved from the authenticated user's JWT, never
        // from a client-supplied id, so an agent can only ever scope to themselves.
        var assignedToUserId = assignedToMe
            ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;
        // Caller identity is also passed so the service can enforce Agent
        // scoping server-side (Phase 6): an Agent only sees their own + unassigned
        // cases regardless of any query param. Admin is unaffected.
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        return await _service.GetAllAsync(status, priority, categoryId, from, to, overdue, assignedToUserId, callerRole, callerUserId);
    }

    /// <summary>Gets a case by id.</summary>
    /// <param name="id">Case id.</param>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CaseDto>> GetById(int id)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var c = await _service.GetByIdAsync(id, callerRole, callerUserId);
        return c is null ? NotFound() : Ok(c);
    }

    /// <summary>
    /// Agent "Messages" tab: cases assigned to the calling agent that have at
    /// least one comment, each summarised with the latest comment and an
    /// unread flag. Agent-only — Admin's global view is a later phase.
    /// </summary>
    /// <returns>Conversation summaries, most-recent activity first.</returns>
    [HttpGet("my-conversations")]
    [Authorize(Roles = "Agent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ConversationSummaryDto>> MyConversations()
    {
        var agentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Missing user id claim.");
        return await _service.GetMyConversationsAsync(agentUserId);
    }

    /// <summary>
    /// Admin "Conversations" view: all cases that have at least one comment,
    /// regardless of assignment. Shows the assigned agent name (or null for
    /// unassigned) and an unread flag per the admin's read state.
    /// Admin-only — Agent-role users get 403.
    /// </summary>
    /// <returns>Conversation summaries, most-recent activity first.</returns>
    [HttpGet("all-conversations")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ConversationSummaryDto>> AllConversations()
    {
        var viewerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Missing user id claim.");
        return await _service.GetAllConversationsAsync(viewerUserId);
    }

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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCaseDto dto)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        await _service.UpdateAsync(id, dto, callerRole, callerUserId);
        return NoContent();
    }

    /// <summary>Deletes a case (Admin only).</summary>
    /// <param name="id">Case id.</param>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _service.DeleteAsync(id, callerRole, callerUserId);
        return NoContent();
    }

    /// <summary>
    /// Returns the shared comment thread for a case (staff can read any case's
    /// thread). 404 if the case does not exist.
    /// </summary>
    /// <param name="id">Case id.</param>
    [HttpGet("{id:int}/comments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CaseCommentDto>>> GetComments(int id)
    {
        var comments = await _comments.GetCommentsAsync(id);
        return comments is null ? NotFound() : Ok(comments);
    }

    /// <summary>
    /// Posts a staff comment to a case's shared thread. The author is taken from
    /// the staff JWT (NameIdentifier claim), never from the request body.
    /// 404 if the case does not exist.
    /// </summary>
    /// <param name="id">Case id.</param>
    /// <param name="dto">Comment body.</param>
    [HttpPost("{id:int}/comments")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CaseCommentDto>> PostComment(int id, [FromBody] CreateCaseCommentDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        var authorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Missing user id claim.");
        try
        {
            var created = await _comments.AddStaffCommentAsync(id, authorUserId, dto.Body);
            return CreatedAtAction(nameof(GetComments), new { id }, created);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException)
        {
            // Empty/whitespace body — the service rejects it; surface as 400
            // rather than letting it bubble to a 500.
            return BadRequest(new ProblemDetails { Title = "Invalid comment body." });
        }
    }

    /// <summary>
    /// Marks a conversation as read for the calling agent. Upserts a
    /// <c>ConversationReadState</c> record so the Messages tab will no longer
    /// flag this case as unread.
    /// </summary>
    /// <param name="id">Case id.</param>
    [HttpPost("{id:int}/conversations/mark-read")]
    [Authorize(Roles = "Admin,Agent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkConversationRead(int id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Missing user id claim.");
        await _service.MarkConversationReadAsync(id, userId);
        return NoContent();
    }
}
