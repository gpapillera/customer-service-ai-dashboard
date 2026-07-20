using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CustomerService.Application.Services;

/// <summary>
/// Routes a <see cref="Notification"/> to the sender that handles its
/// <see cref="Notification.Channel"/>. It takes the concrete leaf senders
/// directly (not <c>IEnumerable&lt;INotificationSender&gt;</c>) so it does not
/// create a circular dependency by depending on itself. Enabling a channel is
/// a config change, and adding a brand-new channel is just a new sender class
/// plus one more constructor parameter here.
/// See docs/DIY.md §7 for the notification flow.
/// </summary>
public class CompositeNotificationSender : INotificationSender
{
    private readonly Dictionary<NotificationChannel, INotificationSender> _byChannel;
    private readonly ILogger<CompositeNotificationSender> _logger;

    /// <summary>Initializes a new <see cref="CompositeNotificationSender"/>.</summary>
    /// <param name="inApp">In-app sender.</param>
    /// <param name="email">Email sender.</param>
    /// <param name="sms">SMS sender.</param>
    /// <param name="logger">Logger.</param>
    public CompositeNotificationSender(
        InAppNotificationSender inApp,
        EmailNotificationSender email,
        SmsNotificationSender sms,
        ILogger<CompositeNotificationSender> logger)
    {
        _logger = logger;
        _byChannel = new()
        {
            [NotificationChannel.InApp] = inApp,
            [NotificationChannel.Email] = email,
            [NotificationChannel.Sms] = sms,
        };
    }

    /// <inheritdoc/>
    public Task SendAsync(Notification notification)
    {
        if (_byChannel.TryGetValue(notification.Channel, out var sender))
        {
            return sender.SendAsync(notification);
        }

        _logger.LogWarning(
            "No INotificationSender handles channel {Channel}; skipping notification #{Id}",
            notification.Channel, notification.Id);
        return Task.CompletedTask;
    }
}
