using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CustomerService.Application.Services;

/// <summary>
/// Demo Email sender. The project ships with no real SMTP server, so this logs
/// the message and appends a line to the Email outbox file
/// (<see cref="NotificationOptions.OutboxPath"/>/emails.log) so delivery is
/// observable and verifiable offline. Swap this for a real SMTP/MailKit sender
/// later without changing the rest of the system — the
/// <see cref="INotificationSender"/> contract and the
/// <see cref="CompositeNotificationSender"/> routing stay the same.
/// See docs/DIY.md §7 for the notification flow.
/// </summary>
[HandlesChannel(NotificationChannel.Email)]
public class EmailNotificationSender : INotificationSender
{
    private readonly ILogger<EmailNotificationSender> _logger;
    private readonly NotificationOptions _options;
    private readonly IRepository<Notification> _notifications;

    /// <summary>Initializes a new <see cref="EmailNotificationSender"/>.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Notification options (outbox path).</param>
    /// <param name="notifications">Notification repository (persists a row so de-dup is uniform across channels).</param>
    public EmailNotificationSender(
        ILogger<EmailNotificationSender> logger,
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
        // Recipient resolution happens upstream (NotificationService): overdue
        // emails target the assigned agent, resolved emails target the customer.
        // When there is no recipient we must NOT guess one — skip and make it
        // visible instead (per business rules). We do not persist a row for a
        // skipped send, so the background job will re-evaluate the case later
        // (e.g. once it gets assigned) rather than treating it as "done".
        if (string.IsNullOrWhiteSpace(notification.Recipient))
        {
            var reason = notification.Type == NotificationType.CaseResolved
                ? "customer has no email"
                : "case is unassigned (no agent email)";
            _logger.LogWarning(
                "EMAIL skipped for case #{CaseId} ({Type}): {Reason}.", notification.CaseId, notification.Type, reason);
            AppendToOutbox("emails.log",
                $"[{notification.CreatedAtUtc:u}] SKIPPED: case #{notification.CaseId} ({notification.Type}) — {reason}");
            return;
        }

        // Persist a row so the (CaseId, Channel, Type) de-dup in
        // NotificationService covers Email too (the in-app center filters these
        // out by channel).
        await _notifications.AddAsync(notification);
        await _notifications.SaveChangesAsync();

        var recipient = notification.Recipient;
        var line = $"[{notification.CreatedAtUtc:u}] TO:{recipient} SUBJECT:{notification.Title} BODY:{notification.Message}";
        _logger.LogInformation(
            "EMAIL -> {Recipient}: {Title} — {Message}", recipient, notification.Title, notification.Message);
        AppendToOutbox("emails.log", line);
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
            _logger.LogWarning(ex, "Failed to write Email outbox line to {Path}", _options.OutboxPath);
        }
    }
}
