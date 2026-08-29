using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;

namespace CustomerService.Application.Services;

/// <summary>
/// In-app notification sender: persists a <see cref="Notification"/> row so it
/// can be surfaced in the notification center. Email delivery is handled by
/// <see cref="EmailNotificationSender"/> (Gmail SMTP) behind the same
/// <see cref="INotificationSender"/> seam.
/// See docs/DIY.md §7 for the notification flow.
/// </summary>
public class InAppNotificationSender : INotificationSender
{
    private readonly IRepository<Notification> _notifications;

    /// <summary>Initializes a new <see cref="InAppNotificationSender"/>.</summary>
    /// <param name="notifications">Notification repository.</param>
    public InAppNotificationSender(IRepository<Notification> notifications)
    {
        _notifications = notifications;
    }

    /// <inheritdoc/>
    public async Task SendAsync(Notification notification)
    {
        await _notifications.AddAsync(notification);
        await _notifications.SaveChangesAsync();
    }
}
