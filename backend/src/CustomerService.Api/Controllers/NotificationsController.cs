using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Serves in-app notifications (overdue follow-up alerts) and lets the client
/// mark them read. Generation is triggered on demand so the demo stays
/// runnable without a background worker.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _service;
    private readonly IEmailTestService _emailTestService;

    /// <summary>Initializes a new <see cref="NotificationsController"/>.</summary>
    /// <param name="service">Notification service.</param>
    /// <param name="emailTestService">Email connectivity test service.</param>
    public NotificationsController(INotificationService service, IEmailTestService emailTestService)
    {
        _service = service;
        _emailTestService = emailTestService;
    }

    /// <summary>Ensures overdue notifications exist, then returns the summary.</summary>
    /// <returns>The notification summary (unread count + recent).</returns>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<NotificationSummaryDto> GetSummary()
    {
        await _service.GenerateOverdueAsync();
        var recipientUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return await _service.GetSummaryAsync(recipientUserId);
    }

    /// <summary>Returns all notifications, newest first.</summary>
    /// <returns>The notification list.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<NotificationDto>> GetAll()
    {
        await _service.GenerateOverdueAsync();
        var recipientUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return await _service.GetAllAsync(recipientUserId);
    }

    /// <summary>Marks a single notification as read.</summary>
    /// <param name="id">Notification id.</param>
    /// <returns>204 if updated, 404 if not found.</returns>
    [HttpPost("{id}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(int id)
    {
        var ok = await _service.MarkReadAsync(id);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Marks every notification as read.</summary>
    /// <returns>204 after updating.</returns>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead()
    {
        await _service.MarkAllReadAsync();
        return NoContent();
    }

    /// <summary>
    /// Tests SMTP connectivity and credentials. Optionally sends a test email.
    /// Admin-only endpoint for diagnosing email notification failures (e.g.
    /// Gmail BadCredentials).
    /// </summary>
    /// <param name="request">
    /// Optional body: <c>{ "testRecipient": "someone@example.com" }</c>.
    /// If omitted or null, only tests connect + authenticate (no email sent).
    /// </param>
    /// <returns>Diagnostic result with connection, auth, and send status.</returns>
    [HttpPost("email/test")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(Application.Interfaces.EmailTestResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailRequest? request)
    {
        var result = await _emailTestService.TestSmtpAsync(request?.TestRecipient);
        return Ok(result);
    }
}

/// <summary>Request body for the email test endpoint.</summary>
public sealed class TestEmailRequest
{
    /// <summary>
    /// Optional recipient address. When provided, a real test email is sent.
    /// When null/empty, only the SMTP connect + authenticate handshake is tested.
    /// </summary>
    public string? TestRecipient { get; set; }
}
