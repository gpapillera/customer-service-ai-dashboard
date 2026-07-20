using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace CustomerService.Application.Services;

/// <summary>
/// Real Email sender backed by MailKit (the maintained .NET SMTP library;
/// <see cref="System.Net.Mail.SmtpClient"/> is obsolete). It connects to the
/// configured SMTP server (Gmail by default) and delivers the message, while
/// keeping the existing <c>emails.log</c> audit trail so delivery stays
/// observable offline.
///
/// The <see cref="INotificationSender"/> contract and the routing/dedup/trigger
/// logic upstream (NotificationService, OverdueEmailHostedService,
/// CaseService.UpdateAsync) are untouched — this class only changes HOW the
/// email is delivered. See docs/DIY.md §7 for the notification flow.
/// </summary>
[HandlesChannel(NotificationChannel.Email)]
public class EmailNotificationSender : INotificationSender
{
    private readonly ILogger<EmailNotificationSender> _logger;
    private readonly NotificationOptions _options;
    private readonly EmailOptions _emailOptions;
    private readonly IRepository<Notification> _notifications;
    private readonly IHostEnvironment _environment;

    /// <summary>Initializes a new <see cref="EmailNotificationSender"/>.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Notification options (outbox path).</param>
    /// <param name="emailOptions">SMTP / sender configuration.</param>
    /// <param name="notifications">Notification repository (persists a row so de-dup is uniform across channels).</param>
    /// <param name="environment">Host environment (used for the dev recipient override).</param>
    public EmailNotificationSender(
        ILogger<EmailNotificationSender> logger,
        NotificationOptions options,
        EmailOptions emailOptions,
        IRepository<Notification> notifications,
        IHostEnvironment environment)
    {
        _logger = logger;
        _options = options;
        _emailOptions = emailOptions;
        _notifications = notifications;
        _environment = environment;
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

        // In Development, redirect to a controlled inbox so we never spam real
        // customers/agents while testing. The original recipient is preserved in
        // the body and an X-Original-Recipient header for verification.
        var originalRecipient = notification.Recipient;
        var effectiveRecipient = originalRecipient;
        var devRedirected = false;
        if (_environment.IsDevelopment()
            && !string.IsNullOrWhiteSpace(_emailOptions.DevOverrideRecipient)
            && !string.Equals(_emailOptions.DevOverrideRecipient, originalRecipient, StringComparison.OrdinalIgnoreCase))
        {
            effectiveRecipient = _emailOptions.DevOverrideRecipient!;
            devRedirected = true;
        }

        var (subject, body) = BuildContent(notification, originalRecipient);

        try
        {
            await SendViaSmtpAsync(effectiveRecipient, subject, body, originalRecipient);
            var audit = $"[{notification.CreatedAtUtc:u}] SENT: case #{notification.CaseId} ({notification.Type}) TO:{effectiveRecipient}"
                + (devRedirected ? $" [DEV-REDIRECT from:{originalRecipient}]" : "")
                + $" SUBJECT:{subject}";
            _logger.LogInformation(
                "EMAIL sent -> {Recipient} (case #{CaseId}, {Type}).", effectiveRecipient, notification.CaseId, notification.Type);
            AppendToOutbox("emails.log", audit);
        }
        catch (Exception ex)
        {
            // A send failure must never crash the overdue job or the
            // status-update flow that called us. Log clearly and keep the audit
            // trail, then swallow.
            _logger.LogError(ex,
                "EMAIL FAILED for case #{CaseId} ({Type}) intended for {Recipient} (effective {EffectiveRecipient}): {Message}",
                notification.CaseId, notification.Type, originalRecipient, effectiveRecipient, ex.Message);
            AppendToOutbox("emails.log",
                $"[{notification.CreatedAtUtc:u}] FAILED: case #{notification.CaseId} ({notification.Type}) TO:{effectiveRecipient} (intended:{originalRecipient}) ERROR:{ex.Message}");
        }
    }

    /// <summary>
    /// Builds type-specific subject/body. Content differs by
    /// <see cref="NotificationType"/> because a human will actually read these
    /// now: overdue alerts are agent-facing and operational; resolved/closed
    /// confirmations are customer-facing and kept professional and simple.
    /// </summary>
    private static (string Subject, string Body) BuildContent(Notification notification, string originalRecipient)
    {
        if (notification.Type == NotificationType.CustomerInvite)
        {
            // Customer-facing invite: plain-language explanation + the link.
            // The link is carried in the message body (set by CustomerAuthService).
            var subject = "You've been invited to the Customer Portal";
            var body = $"Hello,\n\n"
                + $"{notification.Message}\n\n"
                + $"If you weren't expecting this invitation, you can safely ignore this email.\n\n"
                + $"Thank you,\nCustomer Service Team";
            return (subject, body);
        }

        if (notification.Type == NotificationType.CaseResolved)
        {
            var status = notification.Title.Replace("Case ", "", StringComparison.OrdinalIgnoreCase).Trim();
            var subject = $"Your case has been {status}: {notification.Message}";
            // Customer-facing: no internal jargon. Keep it short and reassuring.
            var body = $"Hello,\n\n"
                + $"Your support case #{notification.CaseId} \"{ExtractSubject(notification.Message)}\" has been marked {status}.\n\n"
                + $"If you have any further questions, simply reply to this email or open a new request and we'll be happy to help.\n\n"
                + $"Thank you for contacting us,\nCustomer Service Team";
            return (subject, body);
        }

        // CaseOverdue (agent-facing).
        var overdueSubject = $"Case #{notification.CaseId} is overdue: {ExtractSubject(notification.Message)}";
        var overdueBody = $"Hello,\n\n"
            + $"A follow-up on case #{notification.CaseId} is overdue.\n\n"
            + $"{notification.Message}\n\n"
            + $"Please review and follow up at your earliest convenience.\n\n"
            + $"— Customer Service Dashboard";
        return (overdueSubject, overdueBody);
    }

    /// <summary>
    /// Extracts the human-readable case subject from the stored message text
    /// (which is formatted as 'Case #id "subject" for ...'). Falls back to the
    /// raw message when the pattern is not present.
    /// </summary>
    private static string ExtractSubject(string message)
    {
        // Pattern: Case #3 "API returning 500 errors" for Pedro Penduko ...
        var start = message.IndexOf('"');
        var end = message.IndexOf('"', start + 1);
        if (start >= 0 && end > start)
        {
            return message.Substring(start + 1, end - start - 1);
        }
        return message;
    }

    /// <summary>
    /// Connects to the SMTP server via MailKit and delivers the message. All
    /// network/auth operations are wrapped by the caller's try/catch.
    /// </summary>
    private async Task SendViaSmtpAsync(string to, string subject, string body, string originalRecipient)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_emailOptions.SenderDisplayName, _emailOptions.SenderEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        // Keep the real intended recipient visible for verification even when
        // dev-redirected.
        if (!string.Equals(to, originalRecipient, StringComparison.OrdinalIgnoreCase))
        {
            message.Headers.Add("X-Original-Recipient", originalRecipient);
        }
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_emailOptions.SmtpHost, _emailOptions.SmtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(_emailOptions.SenderEmail, _emailOptions.SenderPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
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
