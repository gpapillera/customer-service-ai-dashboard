using CustomerService.Domain.Entities;

namespace CustomerService.Application.Options;

/// <summary>
/// Configuration for outbound notifications. Controls which channels overdue
/// follow-up alerts are delivered on, and where the demo Email/SMS senders
/// write their outbox files. Bound from the "Notifications" section of
/// appsettings.json.
/// </summary>
public class NotificationOptions
{
    /// <summary>
    /// Channels to deliver overdue-follow-up alerts on. Defaults to InApp only
    /// so the demo behaves exactly as before until you opt in to Email/SMS.
    /// </summary>
    public List<NotificationChannel> Channels { get; set; } = new() { NotificationChannel.InApp };

    /// <summary>
    /// Directory (relative to the content root) where the demo Email/SMS
    /// senders append outbox lines. Defaults to "notifications".
    /// </summary>
    public string OutboxPath { get; set; } = "notifications";

    /// <summary>
    /// How often (in minutes) the background overdue-email worker scans for
    /// overdue cases. Configurable so it is never hardcoded. Defaults to 30.
    /// </summary>
    public double OverdueCheckIntervalMinutes { get; set; } = 30;
}
