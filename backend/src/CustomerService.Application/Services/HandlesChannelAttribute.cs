using CustomerService.Domain.Entities;

namespace CustomerService.Application.Services;

/// <summary>
/// Declares which <see cref="NotificationChannel"/> a concrete
/// <see cref="INotificationSender"/> handles. The
/// <see cref="CompositeNotificationSender"/> reads this attribute to route a
/// notification to the right sender by its <see cref="Notification.Channel"/>.
/// Adding a new delivery channel is just: write a sender class, mark it with
/// this attribute, and enable the channel in config — no other code changes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class HandlesChannelAttribute : Attribute
{
    /// <summary>The channel this sender handles.</summary>
    public NotificationChannel Channel { get; }

    /// <summary>Initializes the attribute.</summary>
    /// <param name="channel">The handled channel.</param>
    public HandlesChannelAttribute(NotificationChannel channel) => Channel = channel;
}
