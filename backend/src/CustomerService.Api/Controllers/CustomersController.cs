using System.Security.Claims;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Optional request body for <c>POST /api/customers/restore/{id}</c>. When
/// <see cref="CaseIds"/> is null, all of the customer's soft-deleted cases are
/// restored alongside the account; an empty array restores none of them
/// (account only); a non-empty list restores only the listed cases.
/// </summary>
public sealed record RestoreCustomerBody
{
    /// <summary>Case ids to restore. Null restores all, empty restores none.</summary>
    public List<int>? CaseIds { get; init; }
}

/// <summary>
/// CRUD endpoints for customers, plus name/email/phone search.
/// See docs/DIY.md §5 for the customer management walkthrough.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _service;
    private readonly ICustomerAuthService _auth;
    private readonly IViewEventService _viewEvents;

    /// <summary>Initializes a new <see cref="CustomersController"/>.</summary>
    /// <param name="service">Customer service.</param>
    /// <param name="auth">Customer auth service (invites).</param>
    /// <param name="viewEvents">Viewed/opened audit service.</param>
    public CustomersController(ICustomerService service, ICustomerAuthService auth, IViewEventService viewEvents)
    {
        _service = service;
        _auth = auth;
        _viewEvents = viewEvents;
    }

    /// <summary>Lists all customers.</summary>
    /// <param name="hasAccount">Optional: filter by account existence (true=has account, false=no account).</param>
    /// <param name="sortBy">Optional: sort field ("name" or "activity"). Default "name".</param>
    /// <param name="sortDirection">Optional: sort direction ("asc" or "desc"). Default "asc".</param>
    /// <returns>All customers (scoped to the caller's shared cases for an Agent).</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CustomerDto>> GetAll(
        [FromQuery] bool? hasAccount = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        return await _service.GetAllAsync(callerRole, callerUserId, hasAccount, sortBy, sortDirection);
    }

    /// <summary>Gets a customer by id.</summary>
    /// <param name="id">Customer id.</param>
    /// <returns>The customer or 404 (403 if an Agent doesn't share a case).</returns>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerDto>> GetById(int id)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var c = await _service.GetByIdAsync(id, callerRole, callerUserId);
        return c is null ? NotFound() : Ok(c);
    }

    /// <summary>Searches customers by name/email/phone.</summary>
    /// <param name="term">Search term.</param>
    /// <param name="hasAccount">Optional: filter by account existence.</param>
    /// <param name="sortBy">Optional: sort field ("name" or "activity").</param>
    /// <param name="sortDirection">Optional: sort direction ("asc" or "desc").</param>
    /// <returns>Matching customers (scoped to the caller's shared cases for an Agent).</returns>
    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CustomerDto>> Search(
        [FromQuery] string? term,
        [FromQuery] bool? hasAccount = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        return await _service.SearchAsync(term, callerRole, callerUserId, hasAccount, sortBy, sortDirection);
    }

    /// <summary>
    /// Returns a customer's case history. For an Agent caller, only the cases
    /// assigned to them are returned (never another agent's cases with the same
    /// customer). Admin sees the full history.
    /// </summary>
    /// <param name="id">Customer id.</param>
    [HttpGet("{id:int}/cases")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CaseDto>>> GetCustomerCases(int id)
    {
        // Reuse the same scoping as GetById: an Agent must share a case with
        // the customer before seeing their history.
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var customer = await _service.GetByIdAsync(id, callerRole, callerUserId);
        if (customer is null) return NotFound();
        var cases = await _service.GetCustomerCaseHistoryAsync(id, callerRole, callerUserId);
        return Ok(cases);
    }

    /// <summary>Returns every email sent to a customer (account invites/resets/manual + case emails), newest first.</summary>
    /// <param name="id">Customer id.</param>
    [HttpGet("{id:int}/emails")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> GetCustomerEmails(int id)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var customer = await _service.GetByIdAsync(id, callerRole, callerUserId);
        if (customer is null) return NotFound();
        var emails = await _service.GetCustomerEmailsAsync(id, callerRole, callerUserId);
        return Ok(emails);
    }

    /// <summary>Returns the merged case + account activity timeline for a customer, newest first.</summary>
    /// <param name="id">Customer id.</param>
    [HttpGet("{id:int}/activity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CustomerActivityItemDto>>> GetCustomerActivity(int id)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var customer = await _service.GetByIdAsync(id, callerRole, callerUserId);
        if (customer is null) return NotFound();
        var activity = await _service.GetCustomerActivityAsync(id, callerRole, callerUserId);
        return Ok(activity);
    }

    /// <summary>
    /// Records that the calling user viewed/opened this customer's detail page.
    /// Coalesced per viewer by a 10-minute cooldown (see <c>ViewEventService</c>)
    /// so refreshes/back-navigation don't flood the audit. Returns 200 with the
    /// created row, or 204 when the view was coalesced into a recent one.
    /// </summary>
    /// <param name="id">Customer id.</param>
    [HttpPost("{id:int}/view")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RecordView(int id)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var customer = await _service.GetByIdAsync(id, callerRole, callerUserId);
        if (customer is null) return NotFound();
        var name = User.FindFirst(ClaimTypes.Name)?.Value ?? callerRole ?? "Staff";
        var created = await _viewEvents.RecordViewAsync("Customer", id, callerUserId, name, callerRole);
        return created is null ? NoContent() : Ok(created);
    }

    /// <summary>Creates a customer.</summary>
    /// <param name="dto">Create payload.</param>
    /// <returns>The created customer.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Updates a customer.</summary>
    /// <param name="id">Customer id.</param>
    /// <param name="dto">Update payload.</param>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
    {
        if (id != dto.Id) return BadRequest();
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        await _service.UpdateAsync(dto, callerRole, callerUserId);
        return NoContent();
    }

    /// <summary>Deletes a customer (Admin only).</summary>
    /// <param name="id">Customer id.</param>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        try
        {
            await _service.DeleteAsync(id, callerRole);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Returns the soft-deleted customers in the recycle bin (Admin only).
    /// Purged customers are excluded. Each row carries its deleted-state
    /// metadata (deletedAt, deletedBy, purged) so the drawer can render
    /// without a second call.
    /// </summary>
    [HttpGet("recycle-bin")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CustomerDto>> GetRecycleBin()
    {
        return await _service.GetDeletedAsync();
    }

    /// <summary>
    /// Returns the soft-deleted (non-purged) cases belonging to a specific
    /// customer — backs the account-restore case-picker dialog (Admin only).
    /// </summary>
    [HttpGet("{id:int}/deleted-cases")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CaseDto>> GetCustomerDeletedCases(int id)
    {
        return await _service.GetDeletedCasesAsync(id);
    }

    /// <summary>
    /// Restores a soft-deleted customer from the recycle bin, optionally
    /// restoring a selected subset of its soft-deleted cases (Admin only).
    /// </summary>
    /// <param name="id">Customer id.</param>
    /// <param name="body">
    /// Optional payload: <c>{ "caseIds": [int, ...] }</c>. Omit or send null
    /// to restore all of the customer's soft-deleted cases; send an empty array
    /// to restore the account only (no cases); send a non-empty list to restore
    /// only those cases.
    /// </param>
    [HttpPost("restore/{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(int id, [FromBody] RestoreCustomerBody? body = null)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        try
        {
            await _service.RestoreAsync(id, body?.CaseIds, callerUserId);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Permanently purges a soft-deleted customer (keep-row anonymize, Admin
    /// only). Irreversible PII erasure — the row stays for audit/FK integrity.
    /// </summary>
    /// <param name="id">Customer id.</param>
    [HttpPost("purge/{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purge(int id)
    {
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        try
        {
            await _service.PurgeAsync(id, callerRole);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Sends a customer-portal invite email. Both Admins and Agents may
    /// trigger this (business decision). Generates a fresh invite token,
    /// overwriting any unused prior invite, and emails the activation link.
    /// </summary>
    /// <param name="id">Customer id.</param>
    /// <returns>204 No Content on success, 400 if the customer has no email, 404 if not found.</returns>
    [HttpPost("{id:int}/invite")]
    [Authorize(Roles = "Admin,Agent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendInvite(int id)
    {
        try
        {
            await _auth.SendInviteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
