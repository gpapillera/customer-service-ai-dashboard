using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace CustomerService.Application.Services;

/// <summary>
/// Routes a <see cref="Notification"/> to the registered sender that handles
/// its <see cref="Notification.Channel"/> (each marked with
/// <see cref="HandlesChannelAttribute"/>). This is the single
/// <see cref="INotificationSender"/> the app consumes; enabling a channel is a
/// config change, and adding a brand-new channel is just a new sender class.
/// See docs/DIY.md §7 for the notification flow.
/// </summary>
public class CompositeNotificationSender : INotificationSender
{
    private readonly Dictionary<NotificationChannel, INotificationSender> _byChannel;
    private readonly ILogger<CompositeNotificationSender> _logger;

    /// <summary>Initializes a new <see cref="CompositeNotificationSender"/>.</summary>
    /// <param name="senders">All registered <see cref="INotificationSender"/> instances.</param>
    /// <param name="logger">Logger.</param>
    public CompositeNotificationSender(IEnumerable<INotificationSender> senders, ILogger<CompositeNotificationSender> logger)
    {
        _logger = logger;
        _byChannel = senders
            .Where(s => s is not CompositeNotificationSender) // never route to ourselves
            .Select(s => (sender: s, attr: s.GetType().GetCustomAttribute<HandlesChannelAttribute>()))
            .Where(x => x.attr is not null)
            .ToDictionary(x => x.attr!.Channel, x => x.sender);
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
