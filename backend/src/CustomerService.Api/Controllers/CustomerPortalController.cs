using System.Security.Claims;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Customer-portal case endpoints. Every endpoint is <c>[Authorize(Roles="Customer")]</c>
/// and derives the customer id strictly from the JWT <c>CustomerId</c> claim —
/// never from a query/route value supplied by the client. Cases are scoped to
/// the calling customer; ownership mismatches and missing cases both return 404
/// (anti-enumeration). See docs/DIY.md §8.
/// </summary>
[ApiController]
[Route("api/customer-portal")]
[Authorize(Roles = "Customer")]
public class CustomerPortalController : ControllerBase
{
    private readonly IRepository<Case> _cases;
    private readonly ICaseCommentService _comments;
    private readonly ICaseService _caseService;
    private readonly ICustomerAuthService _auth;
    private readonly INotificationService _notifications;

    /// <summary>Initializes a new <see cref="CustomerPortalController"/>.</summary>
    public CustomerPortalController(
        IRepository<Case> cases,
        ICaseCommentService comments,
        ICaseService caseService,
        ICustomerAuthService auth,
        INotificationService notifications)
    {
        _cases = cases;
        _comments = comments;
        _caseService = caseService;
        _auth = auth;
        _notifications = notifications;
    }

    /// <summary>Returns only the cases owned by the calling customer.</summary>
    [HttpGet("cases")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CustomerCaseSummaryDto>> GetMyCases()
    {
        var customerId = GetCustomerId();
        var owned = await _cases.Query()
            .Where(c => c.CustomerId == customerId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new CustomerCaseSummaryDto
            {
                Id = c.Id,
                Subject = c.Subject,
                Status = c.Status,
                CreatedAtUtc = c.CreatedAtUtc,
            })
            .ToListAsync();

        return owned;
    }

    /// <summary>
    /// Creates a case on behalf of the calling customer. The <c>CustomerId</c>
    /// is taken strictly from the JWT claim — a client-supplied id is never
    /// trusted. The case is created via the shared <see cref="ICaseService"/>
    /// so it runs the SAME AI-priority-prediction wiring as staff-created
    /// cases (PredictedPriority is set internally), but the customer-facing
    /// response deliberately omits priority/AI fields, consistent with the
    /// GET endpoints. Status defaults to New and the case is left unassigned
    /// for staff to pick up.
    /// </summary>
    [HttpPost("cases")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerCaseSummaryDto>> CreateCase([FromBody] CreateCustomerCaseDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var customerId = GetCustomerId();
        try
        {
            // Reuse the staff creation path (ML priority prediction included).
            var created = await _caseService.CreateAsync(new CreateCaseDto
            {
                Subject = dto.Subject,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                CustomerId = customerId,
                AssignedToUserId = null,
                Priority = null,
            });

            var summary = new CustomerCaseSummaryDto
            {
                Id = created.Id,
                Subject = created.Subject,
                Status = created.Status,
                CreatedAtUtc = created.CreatedAtUtc,
            };
            return CreatedAtAction(nameof(GetMyCase), new { id = created.Id }, summary);
        }
        catch (KeyNotFoundException)
        {
            // Customer or category missing — surface as 404 (the customer
            // record should always exist for an authenticated customer).
            return NotFound();
        }
    }

    /// <summary>
    /// Returns a single case owned by the calling customer. A non-owned or
    /// missing case returns 404 in BOTH cases (anti-enumeration).
    /// </summary>
    [HttpGet("cases/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerCaseDetailDto>> GetMyCase(int id)
    {
        var customerId = GetCustomerId();
        var c = await _cases.Query()
            .Include(x => x.Comments)!
            .ThenInclude(x => x.AuthorUser)
            .Include(x => x.Comments)!
            .ThenInclude(x => x.AuthorCustomer)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customerId);

        // 404 for both "not yours" and "doesn't exist" — identical response.
        if (c is null)
        {
            return NotFound();
        }

        var comments = await _comments.GetCommentsAsync(c.Id) ?? Array.Empty<CaseCommentDto>();
        return Ok(new CustomerCaseDetailDto
        {
            Id = c.Id,
            Subject = c.Subject,
            Description = c.Description,
            Status = c.Status,
            CreatedAtUtc = c.CreatedAtUtc,
            ResolvedAtUtc = c.ResolvedAtUtc,
            Comments = comments,
        });
    }

    /// <summary>
    /// Returns the shared comment thread for one of the customer's cases.
    /// 404 for non-owned/missing case (anti-enumeration).
    /// </summary>
    [HttpGet("cases/{id:int}/comments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CaseCommentDto>>> GetComments(int id)
    {
        if (!await OwnsCaseAsync(id))
        {
            return NotFound();
        }
        var comments = await _comments.GetCommentsAsync(id);
        return comments is null ? NotFound() : Ok(comments);
    }

    /// <summary>
    /// Posts a comment authored by the customer themselves. The author is taken
    /// from the JWT claim; a client-supplied author id is never trusted.
    /// 404 for non-owned/missing case (anti-enumeration).
    /// </summary>
    [HttpPost("cases/{id:int}/comments")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CaseCommentDto>> PostComment(int id, [FromBody] CreateCaseCommentDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        if (!await OwnsCaseAsync(id))
        {
            return NotFound();
        }

        var customerId = GetCustomerId();
        try
        {
            var created = await _comments.AddCustomerCommentAsync(id, customerId, dto.Body);

            // Fire-and-forget the agent notification. A notification failure must
            // never roll back the comment that already succeeded, so it is wrapped
            // and swallowed — same resilience pattern as the resolved/overdue alerts.
            try
            {
                var caseEntity = await _cases.Query()
                    .Include(c => c.Customer)
                    .FirstOrDefaultAsync(c => c.Id == id);
                if (caseEntity is not null)
                {
                    var customerName = caseEntity.Customer?.Name ?? "a customer";
                    await _notifications.NotifyNewCustomerMessageAsync(caseEntity, customerName);
                }
            }
            catch (Exception ex)
            {
                // Logged by the notification service; do not fail the request.
                _ = ex;
            }

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
    /// Returns the signed-in customer's own profile. The id is taken strictly
    /// from the JWT <c>CustomerId</c> claim — never from a query/route value.
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerProfileDto>> GetProfile()
    {
        var customerId = GetCustomerId();
        return Ok(await _auth.GetProfileAsync(customerId));
    }

    /// <summary>
    /// Updates the signed-in customer's own profile. Only the editable fields
    /// (name/phone/company/address) are accepted; the email (login identity)
    /// and id are never taken from the body. The id is taken strictly from the
    /// JWT <c>CustomerId</c> claim.
    /// </summary>
    [HttpPut("profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateCustomerProfileDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        var customerId = GetCustomerId();
        await _auth.UpdateProfileAsync(customerId, dto);
        return NoContent();
    }

    /// <summary>
    /// Requests a password reset for the signed-in customer. Regenerates the
    /// SAME invite token / expiry fields already used by the invite flow and
    /// emails a reset link; the existing accept-invite endpoint is reused to
    /// actually set the new password. No email lookup is needed because the
    /// customer is already authenticated (id from the JWT claim).
    /// </summary>
    [HttpPost("request-password-reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RequestPasswordReset()
    {
        var customerId = GetCustomerId();
        await _auth.RequestPasswordResetAsync(customerId);
        return NoContent();
    }

    private int GetCustomerId()
    {
        var raw = User.FindFirst("CustomerId")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (raw is null || !int.TryParse(raw, out var id))
        {
            // Should be unreachable given [Authorize(Roles="Customer")], but fail
            // safe rather than impersonate another customer.
            throw new UnauthorizedAccessException("Missing or invalid CustomerId claim.");
        }
        return id;
    }

    private async Task<bool> OwnsCaseAsync(int caseId)
    {
        var customerId = GetCustomerId();
        return await _cases.Query()
            .AnyAsync(c => c.Id == caseId && c.CustomerId == customerId);
    }
}
