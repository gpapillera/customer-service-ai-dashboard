using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Serves the email notification log. Every email sent by
/// <c>EmailNotificationSender</c> is persisted in the Notifications table
/// (Channel == Email). This endpoint lets admins view the full history —
/// recipient, subject, type, status, and timestamp — to verify the email
/// notification system is working end-to-end.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class EmailsController : ControllerBase
{
    private readonly INotificationService _service;

    /// <summary>Initializes a new <see cref="EmailsController"/>.</summary>
    /// <param name="service">Notification service.</param>
    public EmailsController(INotificationService service)
    {
        _service = service;
    }

    /// <summary>Returns the email log, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<NotificationDto>> GetAll()
    {
        return await _service.GetEmailLogAsync();
    }

    /// <summary>Composes and sends an ad-hoc email (Admin-only).</summary>
    [HttpPost("compose")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Compose([FromBody] ComposeEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Recipient))
            return BadRequest("Recipient is required.");
        if (string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest("Subject is required.");
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");

        var dto = await _service.ComposeEmailAsync(request);
        return Ok(dto);
    }
}
