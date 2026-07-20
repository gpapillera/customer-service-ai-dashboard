using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Domain.Entities;
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

    /// <summary>Initializes a new <see cref="EmailNotificationSender"/>.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Notification options (outbox path).</param>
    public EmailNotificationSender(ILogger<EmailNotificationSender> logger, NotificationOptions options)
    {
        _logger = logger;
        _options = options;
    }

    /// <inheritdoc/>
    public Task SendAsync(Notification notification)
    {
        var recipient = notification.Recipient ?? "(no recipient email)";
        var line = $"[{notification.CreatedAtUtc:u}] TO:{recipient} SUBJECT:{notification.Title} BODY:{notification.Message}";
        _logger.LogInformation(
            "EMAIL -> {Recipient}: {Title} — {Message}", recipient, notification.Title, notification.Message);
        AppendToOutbox("emails.log", line);
        return Task.CompletedTask;
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
