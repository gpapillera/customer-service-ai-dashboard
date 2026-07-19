using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;

namespace CustomerService.Application.Services;

/// <summary>
/// In-app notification sender: persists a <see cref="Notification"/> row so it
/// can be surfaced in the notification center. This is the only sender used by
/// the demo; Email/SMS senders can be added later behind
/// <see cref="INotificationSender"/>.
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
