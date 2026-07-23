using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerService.Application.Services;

/// <summary>
/// Generates overdue-follow-up notifications and serves them to the in-app
/// notification center. Generation is idempotent: at most one notification
/// exists per (CaseId, Channel, Type) at any time. Which channels fire is
/// driven by <see cref="NotificationOptions.Channels"/> — InApp by default,
/// with Email/SMS available via the <see cref="INotificationSender"/> seam.
///
/// Business rules (Email channel only):
///  - Overdue (CaseOverdue): goes to the ASSIGNED AGENT's email. If the case
///    is unassigned, it is skipped (logged, never guessed).
///  - Resolved/Closed (CaseResolved): goes to the CUSTOMER's email. If the
///    customer has no email, it is skipped (logged, never guessed).
/// The in-app bell is inherently agent-only (customers have no login), so its
/// audience is unchanged.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IRepository<Case> _cases;
    private readonly IRepository<Notification> _notifications;
    private readonly INotificationSender _sender;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Initializes a new <see cref="NotificationService"/>.</summary>
    /// <param name="cases">Case repository.</param>
    /// <param name="notifications">Notification repository.</param>
    /// <param name="sender">Composite notification sender (routes by channel).</param>
    /// <param name="options">Notification options (enabled channels).</param>
    /// <param name="logger">Logger.</param>
    public NotificationService(
        IRepository<Case> cases,
        IRepository<Notification> notifications,
        INotificationSender sender,
        NotificationOptions options,
        ILogger<NotificationService> logger)
    {
        _cases = cases;
        _notifications = notifications;
        _sender = sender;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> GenerateOverdueAsync()
    {
        var now = DateTime.UtcNow;

        // Overdue cases: open and needing a follow-up (scheduled deadline missed
        // OR stale with no follow-up). Uses the shared OverduePolicy so this
        // matches the dashboard and the cases filter exactly.
        var allCases = await _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.AssignedToUser)
            .Include(c => c.CallLogs)
            .ToListAsync();
        var overdueCases = allCases
            .Where(c => OverduePolicy.NeedsFollowUp(c, now))
            .ToList();

        if (overdueCases.Count == 0)
        {
            return 0;
        }

        // (CaseId, Channel, Type) triples that already have a notification
        // (read or unread) — never re-notify the same case/channel/type, even
        // after the user marks it read. The Type dimension lets an overdue-agent
        // email and a resolved-customer email coexist for the same case.
        var alreadyNotified = await _notifications.Query()
            .Where(n => n.CaseId.HasValue)
            .Select(n => new { n.CaseId, n.Channel, n.Type })
            .ToListAsync();
        var alreadySet = new HashSet<(int, NotificationChannel, NotificationType)>(
            alreadyNotified.Select(x => (x.CaseId!.Value, x.Channel, x.Type)));

        var channels = _options.Channels.Distinct().ToList();
        var created = 0;
        foreach (var c in overdueCases)
        {
            var daysOverdue = OverduePolicy.DaysOverdue(c, now);
            var customerName = c.Customer?.Name ?? "a customer";
            var body = $"Case #{c.Id} \"{c.Subject}\" for {customerName} is {daysOverdue} day(s) overdue for a follow-up.";

            foreach (var channel in channels)
            {
                if (alreadySet.Contains((c.Id, channel, NotificationType.CaseOverdue)))
                {
                    continue;
                }

                // Recipient resolution (overdue alerts are agent-facing):
                //  - InApp: shown to any agent (no single recipient).
                //  - Email: the ASSIGNED AGENT's email. An unassigned case has
                //    no recipient — skip it (logged) rather than guessing one.
                //  - SMS:   audience is unchanged (customer phone) per spec.
                var recipient = channel switch
                {
                    NotificationChannel.InApp => null,
                    NotificationChannel.Email => c.AssignedToUser?.Email,
                    NotificationChannel.Sms => c.Customer?.Phone,
                    _ => null,
                };

                if (channel != NotificationChannel.InApp && string.IsNullOrWhiteSpace(recipient))
                {
                    var reason = channel == NotificationChannel.Email
                        ? "case is unassigned (no agent email)"
                        : "customer has no phone";
                    _logger.LogWarning(
                        "Overdue {Channel} skipped for case #{CaseId}: {Reason}.", channel, c.Id, reason);
                    continue;
                }

                var notification = new Notification
                {
                    Title = "Overdue follow-up",
                    Message = body,
                    Channel = channel,
                    Type = NotificationType.CaseOverdue,
                    Status = NotificationStatus.Unread,
                    CreatedAtUtc = now,
                    Link = channel == NotificationChannel.InApp ? $"/cases/{c.Id}" : null,
                    CaseId = c.Id,
                    Recipient = recipient,
                };
                await _sender.SendAsync(notification);
                created++;
            }
        }

        return created;
    }

    /// <summary>
    /// Sends a resolved/closed confirmation to the CUSTOMER (Email channel
    /// only) when that channel is enabled. Skipped (and logged) when the case
    /// has no customer email. Idempotent on (CaseId, Email, CaseResolved).
    /// Called synchronously from <c>CaseService.UpdateAsync</c> when a case
    /// transitions to Resolved/Closed. A failure here never blocks the status
    /// update itself.
    /// </summary>
    /// <param name="caseEntity">The case that was just resolved/closed.</param>
    /// <returns>The number of notifications created (0 or 1).</returns>
    public async Task<int> NotifyResolvedAsync(Case caseEntity)
    {
        var channels = _options.Channels.Distinct().ToList();
        if (channels.Count == 0)
        {
            return 0;
        }

        var alreadyExists = await _notifications.Query()
            .AnyAsync(n => n.CaseId == caseEntity.Id
                && n.Channel == NotificationChannel.Email
                && n.Type == NotificationType.CaseResolved);
        if (alreadyExists)
        {
            return 0;
        }

        var customerName = caseEntity.Customer?.Name ?? "a customer";
        var body = $"Your case #{caseEntity.Id} \"{caseEntity.Subject}\" has been marked {caseEntity.Status}.";
        var created = 0;

        // Resolved/closed confirmations are customer-facing (Email only). In-app
        // has no customer audience, so we do not generate an in-app row for this
        // type. If the customer has no email we skip (logged) rather than guess.
        foreach (var channel in channels)
        {
            if (channel != NotificationChannel.Email)
            {
                continue;
            }

            var recipient = caseEntity.Customer?.Email;
            if (string.IsNullOrWhiteSpace(recipient))
            {
                _logger.LogWarning(
                    "Resolved {Channel} skipped for case #{CaseId}: customer has no email.", channel, caseEntity.Id);
                continue;
            }

            var notification = new Notification
            {
                Title = $"Case {caseEntity.Status}",
                Message = body,
                Channel = channel,
                Type = NotificationType.CaseResolved,
                Status = NotificationStatus.Unread,
                CreatedAtUtc = DateTime.UtcNow,
                Link = null,
                CaseId = caseEntity.Id,
                Recipient = recipient,
            };
            await _sender.SendAsync(notification);
            created++;
        }

        return created;
    }

    /// <inheritdoc />
    public async Task<int> NotifyNewCustomerMessageAsync(Case caseEntity, string customerName)
    {
        if (caseEntity is null)
        {
            _logger.LogWarning("NewCustomerMessage skipped: caseEntity is null.");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(caseEntity.AssignedToUserId))
        {
            // No agent owns this case, so there is no recipient for the alert.
            // Log clearly and skip — same resilience pattern as overdue/resolved.
            _logger.LogWarning(
                "NewCustomerMessage skipped for case #{CaseId}: case is unassigned (no agent recipient).",
                caseEntity.Id);
            return 0;
        }

        // Idempotent: one open in-app alert per case. If an unread
        // NewCustomerMessage already exists for this case, do not stack another.
        var existing = await _notifications.Query()
            .Where(n => n.CaseId == caseEntity.Id
                && n.Channel == NotificationChannel.InApp
                && n.Type == NotificationType.NewCustomerMessage
                && n.Status == NotificationStatus.Unread)
            .ToListAsync();
        if (existing.Any())
        {
            return 0;
        }

        var notification = new Notification
        {
            Title = "New customer message",
            Message = $"{customerName} sent a new message on case #{caseEntity.Id} \"{caseEntity.Subject}\".",
            Channel = NotificationChannel.InApp,
            Type = NotificationType.NewCustomerMessage,
            Status = NotificationStatus.Unread,
            CreatedAtUtc = DateTime.UtcNow,
            Link = $"/cases/{caseEntity.Id}",
            CaseId = caseEntity.Id,
            Recipient = caseEntity.AssignedToUserId,
        };

        await _sender.SendAsync(notification);
        return 1;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NotificationDto>> GetEmailLogAsync()
    {
        var list = await _notifications.Query()
            .Where(n => n.Channel == NotificationChannel.Email)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => NotificationDto.FromEntity(n))
            .ToListAsync();
        return list;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NotificationDto>> GetAllAsync(string? recipientUserId = null)
    {
        var query = _notifications.Query()
            .Where(n => n.Channel == NotificationChannel.InApp);

        // When a recipient is specified (Agent), only show notifications
        // addressed to this user or broadcast notifications (Recipient null).
        if (!string.IsNullOrWhiteSpace(recipientUserId))
        {
            query = query.Where(n => n.Recipient == recipientUserId || n.Recipient == null);
        }

        var list = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => NotificationDto.FromEntity(n))
            .ToListAsync();
        return list;
    }

    /// <inheritdoc/>
    public async Task<NotificationSummaryDto> GetSummaryAsync(string? recipientUserId = null)
    {
        var query = _notifications.Query()
            .Where(n => n.Status == NotificationStatus.Unread)
            .Where(n => n.Channel == NotificationChannel.InApp);

        // When a recipient is specified (Agent), only count notifications
        // addressed to this user or broadcast notifications (Recipient null).
        if (!string.IsNullOrWhiteSpace(recipientUserId))
        {
            query = query.Where(n => n.Recipient == recipientUserId || n.Recipient == null);
        }

        var unread = await query.ToListAsync();
        var recent = unread
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(5)
            .Select(n => NotificationDto.FromEntity(n))
            .ToList();
        return new NotificationSummaryDto
        {
            UnreadCount = unread.Count,
            Recent = recent,
        };
    }

    /// <inheritdoc/>
    public async Task<bool> MarkReadAsync(int id)
    {
        var n = await _notifications.GetByIdAsync(id);
        if (n is null)
        {
            return false;
        }

        n.Status = NotificationStatus.Read;
        _notifications.Update(n);
        await _notifications.SaveChangesAsync();
        return true;
    }

    /// <inheritdoc/>
    public async Task<int> MarkAllReadAsync()
    {
        var unread = await _notifications.Query()
            .Where(n => n.Status == NotificationStatus.Unread)
            .Where(n => n.Channel == NotificationChannel.InApp)
            .ToListAsync();
        foreach (var n in unread)
        {
            n.Status = NotificationStatus.Read;
            _notifications.Update(n);
        }

        if (unread.Count == 0)
        {
            return 0;
        }

        await _notifications.SaveChangesAsync();
        return unread.Count;
    }
}
