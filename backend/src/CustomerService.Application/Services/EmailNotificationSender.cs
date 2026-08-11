using System.Net.Sockets;
using System.Linq;
using System.Text.RegularExpressions;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly IRepository<Case> _cases;
    private readonly IRepository<Customer> _customers;
    private readonly IRepository<User> _users;
    private readonly IHostEnvironment _environment;
    private readonly IEmailConfigService _emailConfig;
    private readonly string _frontendBaseUrl;

    /// <summary>Initializes a new <see cref="EmailNotificationSender"/>.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Notification options (outbox path).</param>
    /// <param name="emailOptions">SMTP / sender configuration.</param>
    /// <param name="notifications">Notification repository (persists a row so de-dup is uniform across channels).</param>
    /// <param name="cases">Case repository (loads customer/agent for token personalization).</param>
    /// <param name="customers">Customer repository (name lookup for account emails with no case).</param>
    /// <param name="users">Staff user repository (name lookup for staff account emails).</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="emailConfig">Email configuration (allowed domains + test address).</param>
    /// <param name="configuration">App configuration (for FrontendBaseUrl token).</param>
    public EmailNotificationSender(
        ILogger<EmailNotificationSender> logger,
        NotificationOptions options,
        EmailOptions emailOptions,
        IRepository<Notification> notifications,
        IRepository<Case> cases,
        IRepository<Customer> customers,
        IRepository<User> users,
        IHostEnvironment environment,
        IEmailConfigService emailConfig,
        IConfiguration configuration)
    {
        _logger = logger;
        _options = options;
        _emailOptions = emailOptions;
        _notifications = notifications;
        _cases = cases;
        _customers = customers;
        _users = users;
        _environment = environment;
        _emailConfig = emailConfig;
        _frontendBaseUrl = configuration["FrontendBaseUrl"] ?? "http://localhost:4200";
    }

    /// <summary>
    /// Determines the real delivery address for an outbound email. If the
    /// recipient's domain is on the allowed list, the message goes to the
    /// original recipient. Otherwise it is redirected to the configured test
    /// address so the demo never spams real customers/agents. A blank original
    /// recipient always redirects (we never guess an address).
    /// </summary>
    /// <param name="originalRecipient">The intended recipient (may be empty).</param>
    /// <param name="allowedDomains">Lower-cased allowed domain suffixes.</param>
    /// <param name="testEmailAddress">Configured redirect/test address.</param>
    /// <returns>The effective delivery address.</returns>
    public static string ResolveEffectiveRecipient(
        string? originalRecipient,
        ISet<string> allowedDomains,
        string testEmailAddress)
    {
        if (string.IsNullOrWhiteSpace(originalRecipient))
            return testEmailAddress;

        var at = originalRecipient.IndexOf('@');
        if (at < 0 || at == originalRecipient.Length - 1)
            return testEmailAddress;

        var domain = originalRecipient[(at + 1)..].ToLowerInvariant();
        return allowedDomains.Contains(domain) ? originalRecipient : testEmailAddress;
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

        // Recipient routing: listed domains deliver directly; everything else
        // is redirected to the configured test address (never spam real
        // customers/agents). The original recipient is preserved in the body
        // and an X-Original-Recipient header for verification.
        var originalRecipient = notification.Recipient;
        var config = await _emailConfig.GetConfigAsync();
        var allowed = (await _emailConfig.ListDomainsAsync())
            .Select(d => d.Domain.ToLowerInvariant())
            .ToHashSet();
        var effectiveRecipient = ResolveEffectiveRecipient(originalRecipient, allowed, config.TestEmailAddress);
        var devRedirected = !string.Equals(effectiveRecipient, originalRecipient, StringComparison.OrdinalIgnoreCase);

        var (subject, body) = await BuildContentAsync(notification);

        try
        {
            await SendWithRetryAsync(effectiveRecipient, subject, body, originalRecipient, notification.CaseId, notification.Type);
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
            var errorDetail = ClassifySmtpError(ex);
            _logger.LogError(ex,
                "EMAIL FAILED ({ErrorDetail}) for case #{CaseId} ({Type}) intended for {Recipient} (effective {EffectiveRecipient}): {Message}",
                errorDetail, notification.CaseId, notification.Type, originalRecipient, effectiveRecipient, ex.Message);
            AppendToOutbox("emails.log",
                $"[{notification.CreatedAtUtc:u}] FAILED ({errorDetail}): case #{notification.CaseId} ({notification.Type}) TO:{effectiveRecipient} (intended:{originalRecipient}) ERROR:{ex.Message}");
        }
    }

    /// <summary>
    /// Substitutes <c>{{token}}</c> placeholders in a template string using the
    /// provided token map (case-insensitive key match). Unknown tokens are left
    /// as literals so mis-configured templates remain visible rather than
    /// silently dropping content.
    /// </summary>
    /// <param name="template">Subject or body template text.</param>
    /// <param name="tokens">Token name → value map.</param>
    /// <returns>The rendered string.</returns>
    public static string RenderTemplate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? string.Empty;

        var result = template;
        foreach (var (key, value) in tokens)
        {
            result = result.Replace($"{{{{{key}}}}}", value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    /// <summary>
    /// Renders the email subject/body for a notification. Prefers the editable
    /// <see cref="EmailTemplate"/> matching the notification's
    /// <see cref="NotificationType"/> (with personalization tokens filled from
    /// the related case/customer/agent), and falls back to a generic
    /// <see cref="BuildFallbackContent"/> (with a warning log) only when no
    /// template exists for that type.
    /// </summary>
    private async Task<(string Subject, string Body)> BuildContentAsync(
        Notification notification)
    {
        var typeName = notification.Type.ToString();
        var templates = await _emailConfig.ListTemplatesAsync();
        var template = templates.FirstOrDefault(t =>
            string.Equals(t.Type, typeName, StringComparison.OrdinalIgnoreCase));

        if (template is null)
        {
            // No editable template configured for this type. This is a config gap,
            // not a normal path (the seed data ships a template for every type), so
            // warn loudly and fall back to a generic last-resort message rather than
            // a type-specific hard-coded string that could silently diverge from the
            // intended template.
            _logger.LogWarning(
                "No email template configured for NotificationType {Type}. Using a generic fallback subject/body. Add a template of this Type in Email Configuration to take control of the content.",
                typeName);
            return BuildFallbackContent(notification);
        }

        var tokens = await BuildTokenMapAsync(notification);
        var subject = RenderTemplate(template.Subject, tokens);
        var body = EnsureActionLink(RenderTemplate(template.Body, tokens), ResolveActionLink(notification));
        return (subject, body);
    }

    /// <summary>
    /// Resolves the action URL for a notification. Prefers the explicit
    /// <see cref="Notification.Link"/> column, but falls back to the first URL
    /// found in <see cref="Notification.Message"/>. The fallback matters for
    /// rows created before <c>Link</c> was populated (and for any caller that
    /// still only puts the URL in the message): the sender renders the DB
    /// template and discards <c>Message</c>, so without this the link is lost.
    /// </summary>
    /// <param name="notification">The notification being sent.</param>
    /// <returns>The action URL, or null when there is none.</returns>
    public static string? ResolveActionLink(Notification notification) =>
        !string.IsNullOrWhiteSpace(notification.Link)
            ? notification.Link
            : ExtractFirstUrl(notification.Message);

    /// <summary>
    /// Returns the first http/https URL in <paramref name="text"/>, or null.
    /// Trailing sentence punctuation is not part of a URL and is trimmed.
    /// </summary>
    /// <param name="text">Text to scan.</param>
    /// <returns>The first URL found, or null.</returns>
    public static string? ExtractFirstUrl(string? text)
    {
        var match = UrlPattern.Match(text ?? string.Empty);
        return match.Success ? match.Value.TrimEnd('.', ',', ')', '>', '"', '\'', ';', ':') : null;
    }

    private static readonly Regex UrlPattern =
        new(@"(https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// True for notification types whose entire purpose is to deliver a
    /// clickable activation / password-reset URL to a specific recipient
    /// (<see cref="NotificationType.CustomerInvite"/>,
    /// <see cref="NotificationType.CustomerPasswordReset"/>,
    /// <see cref="NotificationType.StaffPasswordReset"/>). For these the
    /// <c>{{portalLink}}</c> token resolves to that deep link (not the portal
    /// homepage) — see <see cref="BuildTokenMapAsync"/>.
    /// </summary>
    private static bool IsAccountActivationType(NotificationType type) =>
        type is NotificationType.CustomerInvite
            or NotificationType.CustomerPasswordReset
            or NotificationType.StaffPasswordReset;

    /// <summary>
    /// Resolves the value substituted for the <c>{{portalLink}}</c> token.
    /// For account-activation / password-reset notifications it is the
    /// per-recipient deep link (the invite/reset URL); otherwise it is the
    /// portal homepage base URL. Falls back to the base URL when an
    /// activation notification has no <paramref name="link"/>.
    /// </summary>
    /// <param name="type">Notification type.</param>
    /// <param name="link">The per-recipient action URL, if any.</param>
    /// <param name="baseUrl">Configured frontend base URL (homepage).</param>
    /// <returns>The <c>{{portalLink}}</c> value.</returns>
    public static string ResolvePortalLink(NotificationType type, string? link, string baseUrl) =>
        IsAccountActivationType(type) ? (link ?? baseUrl) : baseUrl;

    /// <summary>
    /// Guarantees an action URL (activation / password-reset link) reaches the
    /// reader. Templates are operator-editable and pre-existing databases were
    /// seeded before <c>{{actionLink}}</c> existed, so a template can easily
    /// omit it — and a "set your password" email without its link is useless.
    /// When the notification carries a <see cref="Notification.Link"/> that the
    /// rendered body does not already contain, the link is appended.
    /// </summary>
    /// <param name="body">The rendered body.</param>
    /// <param name="link">The action URL, if any.</param>
    /// <returns>The body, with the link appended when it was missing.</returns>
    public static string EnsureActionLink(string body, string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || body.Contains(link, StringComparison.OrdinalIgnoreCase))
            return body;
        return $"{body.TrimEnd()}\n\n{link}";
    }

    /// <summary>
    /// Builds the personalization token map for a notification by loading the
    /// related case (with customer + assigned agent). Missing data resolves to
    /// an empty string so templates degrade gracefully.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> BuildTokenMapAsync(Notification notification)
    {
        var caseSubject = ResolveCaseSubject(notification.Type, notification.Message);
        // {{portalLink}} resolves to the portal HOMEPAGE for case/overdue/resolved
        // emails, but for account-activation / password-reset emails it resolves to
        // the per-recipient deep link (notification.Link) — the invite/reset URL IS
        // "the portal" for that recipient. This keeps operator templates that use
        // {{portalLink}} correct WITHOUT a reseed, and lets the EnsureActionLink
        // safety net see the link is already present (no duplicate appended).
        var portalLink = ResolvePortalLink(notification.Type, notification.Link, _frontendBaseUrl);

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["caseId"] = notification.CaseId?.ToString() ?? string.Empty,
            ["caseSubject"] = caseSubject,
            ["portalLink"] = portalLink,
            // Activation / password-reset URL for this specific notification.
            ["actionLink"] = notification.Link ?? string.Empty,
            ["customerName"] = string.Empty,
            ["customerEmail"] = string.Empty,
            ["caseStatus"] = string.Empty,
            ["agentName"] = string.Empty,
            ["agentEmail"] = string.Empty,
        };

        if (notification.CaseId.HasValue)
        {
            var caseEntity = await _cases.Query()
                .Include(c => c.Customer)
                .Include(c => c.AssignedToUser)
                .FirstOrDefaultAsync(c => c.Id == notification.CaseId.Value);
            if (caseEntity is not null)
            {
                if (caseEntity.Customer is not null)
                {
                    tokens["customerName"] = caseEntity.Customer.Name;
                    tokens["customerEmail"] = caseEntity.Customer.Email;
                }
                tokens["caseStatus"] = caseEntity.Status.ToString();
                if (caseEntity.AssignedToUser is not null)
                {
                    tokens["agentName"] = caseEntity.AssignedToUser.FullName;
                    tokens["agentEmail"] = caseEntity.AssignedToUser.Email;
                }
            }
        }

        // Account emails (invite / password reset) have no CaseId, so the block
        // above cannot fill the name and templates render "Hello ,". Fall back
        // to looking the person up by the recipient address.
        if (string.IsNullOrEmpty(tokens["customerName"]) && !string.IsNullOrWhiteSpace(notification.Recipient))
        {
            var email = notification.Recipient.Trim().ToLower();
            var customer = await _customers.Query().FirstOrDefaultAsync(c => c.Email == email);
            if (customer is not null)
            {
                tokens["customerName"] = customer.Name;
                tokens["customerEmail"] = customer.Email;
            }
            else
            {
                var user = await _users.Query().FirstOrDefaultAsync(u => u.Email == email);
                if (user is not null)
                {
                    tokens["agentName"] = user.FullName;
                    tokens["agentEmail"] = user.Email;
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// Last-resort content used only when no editable <see cref="EmailTemplate"/>
    /// exists for a notification type (a config gap). Kept deliberately generic and
    /// token-light so it can never silently diverge from the intended template the
    /// operator would see in Email Configuration. The DB template is always preferred;
    /// this path should not normally execute because seed data ships a template per type.
    /// </summary>
    private static (string Subject, string Body) BuildFallbackContent(Notification notification)
    {
        var subject = notification.CaseId.HasValue
            ? $"Update on case #{notification.CaseId}"
            : "Update from Customer Service";
        var body = $"Hello,\n\n"
            + $"{notification.Message}\n\n"
            + $"Thank you,\nCustomer Service Team";
        return (subject, body);
    }

    /// <summary>
    /// Resolves the <c>{{caseSubject}}</c> token. Machine-generated messages
    /// use the <c>Case #n "subject"</c> shape, so the quoted part is extracted.
    /// <see cref="NotificationType.AdminManual"/> is free text an admin typed —
    /// extraction there would silently discard everything outside the first
    /// quoted pair, so it is passed through verbatim.
    /// </summary>
    /// <param name="type">The notification type.</param>
    /// <param name="message">The stored message text.</param>
    /// <returns>The subject text for token substitution.</returns>
    public static string ResolveCaseSubject(NotificationType type, string message) =>
        type == NotificationType.AdminManual ? message : ExtractCaseSubject(message);

    /// <summary>
    /// Extracts the human-readable case subject from the stored message text
    /// (formatted as 'Case #id "subject" for ...'). Falls back to the raw
    /// message when the pattern is not present.
    /// </summary>
    private static string ExtractCaseSubject(string message)
    {
        var start = message.IndexOf('"');
        var end = message.IndexOf('"', start + 1);
        if (start >= 0 && end > start)
        {
            return message.Substring(start + 1, end - start - 1);
        }
        return message;
    }

    /// <summary>
    /// Maximum number of SMTP send attempts (including the initial try).
    /// Transient network errors and temporary auth failures are retried with
    /// exponential backoff. Permanent auth failures (bad credentials) are
    /// NOT retried.
    /// </summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// Retries <see cref="SendViaSmtpAsync" /> up to <see cref="MaxRetries" />
    /// times with exponential backoff. Only transient / network errors are
    /// retried — authentication failures (bad credentials) fail immediately.
    /// </summary>
    private async Task SendWithRetryAsync(string to, string subject, string body, string originalRecipient, int? caseId, NotificationType type)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await SendViaSmtpAsync(to, subject, body, originalRecipient);
                if (attempt > 1)
                    _logger.LogInformation(
                        "EMAIL sent on attempt {Attempt}/{Max} for case #{CaseId} ({Type}).",
                        attempt, MaxRetries, caseId, type);
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransientError(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s
                _logger.LogWarning(ex,
                    "EMAIL transient error on attempt {Attempt}/{Max} for case #{CaseId} ({Type}), retrying in {Delay}s...",
                    attempt, MaxRetries, caseId, type, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        // Final attempt — let the exception propagate to the caller.
        await SendViaSmtpAsync(to, subject, body, originalRecipient);
    }

    /// <summary>
    /// Returns <c>true</c> when the exception represents a transient / network
    /// error that may succeed on retry. Returns <c>false</c> for permanent
    /// failures (authentication, malformed address, etc.).
    /// </summary>
    private static bool IsTransientError(Exception ex)
    {
        // MailKit wraps network errors; authentication failures are permanent.
        if (ex is System.Security.Authentication.AuthenticationException)
            return false;

        // IO/network-level errors (timeout, connection reset, DNS failure)
        if (ex is System.IO.IOException)
            return true;
        if (ex is System.Net.Sockets.SocketException)
            return true;
        if (ex is System.Net.Http.HttpRequestException)
            return true;
        if (ex is OperationCanceledException)
            return true;

        // Inner exceptions (MailKit often wraps)
        if (ex.InnerException != null)
            return IsTransientError(ex.InnerException);

        return false;
    }

    /// <summary>
    /// Classifies an SMTP error into a human-readable category for logging
    /// and the outbox audit trail.
    /// </summary>
    private static string ClassifySmtpError(Exception ex)
    {
        if (ex is System.Security.Authentication.AuthenticationException)
            return "AUTH_FAILED — check SenderEmail/SenderPassword (Gmail: use App Password, not account password)";
        if (ex is System.IO.IOException)
            return "NETWORK_IO — connection timed out or reset";
        if (ex is System.Net.Sockets.SocketException)
            return "SOCKET — could not reach SMTP server";
        if (ex.Message.Contains("535", StringComparison.OrdinalIgnoreCase))
            return "SMTP_535 — authentication rejected (invalid credentials or app password revoked)";
        return "UNKNOWN";
    }

    /// <summary>
    /// Connects to the SMTP server via MailKit and delivers the message.
    /// A new <see cref="SmtpClient"/> is created per call — this is the
    /// recommended MailKit pattern (unlike the obsolete System.Net.Mail.SmtpClient).
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
        // Set a connection timeout so we don't hang indefinitely on unreachable servers.
        client.Timeout = 30_000; // 30 seconds
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
