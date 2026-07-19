using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Serves in-app notifications (overdue follow-up alerts) and lets the client
/// mark them read. Generation is triggered on demand so the demo stays
/// runnable without a background worker.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _service;

    /// <summary>Initializes a new <see cref="NotificationsController"/>.</summary>
    /// <param name="service">Notification service.</param>
    public NotificationsController(INotificationService service) => _service = service;

    /// <summary>Ensures overdue notifications exist, then returns the summary.</summary>
    /// <returns>The notification summary (unread count + recent).</returns>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<NotificationSummaryDto> GetSummary()
    {
        await _service.GenerateOverdueAsync();
        return await _service.GetSummaryAsync();
    }

    /// <summary>Returns all notifications, newest first.</summary>
    /// <returns>The notification list.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<NotificationDto>> GetAll()
    {
        await _service.GenerateOverdueAsync();
        return await _service.GetAllAsync();
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
}
