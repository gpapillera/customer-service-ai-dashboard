using CustomerService.Domain.Entities;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// Pluggable notification delivery contract. The demo ships an in-app sender
/// that persists a <see cref="Notification"/> row; the Email sender (Gmail SMTP)
/// also implements this same interface without touching the rest of the system.
/// </summary>
public interface INotificationSender
{
    /// <summary>Delivers a single notification (e.g. persists it / sends it).</summary>
    /// <param name="notification">The notification to deliver.</param>
    /// <returns>A task completing when delivery is finished.</returns>
    Task SendAsync(Notification notification);
}
