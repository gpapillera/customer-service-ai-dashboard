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
}
