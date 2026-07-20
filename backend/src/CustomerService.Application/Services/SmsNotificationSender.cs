using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CustomerService.Application.Services;

/// <summary>
/// Demo SMS sender. The project ships with no real SMS gateway, so this logs
/// the message and appends a line to the SMS outbox file
/// (<see cref="NotificationOptions.OutboxPath"/>/sms.log) so delivery is
/// observable and verifiable offline. Swap this for a real SMS/Twilio sender
/// later without changing the rest of the system — the
/// <see cref="INotificationSender"/> contract and the
/// <see cref="CompositeNotificationSender"/> routing stay the same.
/// See docs/DIY.md §7 for the notification flow.
/// </summary>
[HandlesChannel(NotificationChannel.Sms)]
public class SmsNotificationSender : INotificationSender
{
    private readonly ILogger<SmsNotificationSender> _logger;
    private readonly NotificationOptions _options;
    private readonly IRepository<Notification> _notifications;

    /// <summary>Initializes a new <see cref="SmsNotificationSender"/>.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Notification options (outbox path).</param>
    /// <param name="notifications">Notification repository (persists a row so de-dup is uniform across channels).</param>
    public SmsNotificationSender(
        ILogger<SmsNotificationSender> logger,
        NotificationOptions options,
        IRepository<Notification> notifications)
    {
        _logger = logger;
        _options = options;
        _notifications = notifications;
    }

    /// <inheritdoc/>
    public async Task SendAsync(Notification notification)
    {
        // Persist a row so the (CaseId, Channel) de-dup in NotificationService
        // covers SMS too (the in-app center filters these out by channel).
        await _notifications.AddAsync(notification);
        await _notifications.SaveChangesAsync();

        var recipient = notification.Recipient ?? "(no recipient phone)";
        var line = $"[{notification.CreatedAtUtc:u}] TO:{recipient} BODY:{notification.Message}";
        _logger.LogInformation(
            "SMS -> {Recipient}: {Message}", recipient, notification.Message);
        AppendToOutbox("sms.log", line);
    }

    private void AppendToOutbox(string fileName, string line)
    {
        try
        {
            Directory.CreateDirectory(_options.OutboxPath);
            File.AppendAllLines(Path.Combine(_options.OutboxPath, fileName), new[] { line });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write SMS outbox line to {Path}", _options.OutboxPath);
        }
    }
}
