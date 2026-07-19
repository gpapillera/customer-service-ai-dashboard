using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Generates overdue-follow-up notifications and serves them to the in-app
/// notification center. Generation is idempotent: at most one unread
/// notification exists per overdue case at any time.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IRepository<Case> _cases;
    private readonly IRepository<Notification> _notifications;
    private readonly INotificationSender _sender;

    /// <summary>Initializes a new <see cref="NotificationService"/>.</summary>
    /// <param name="cases">Case repository.</param>
    /// <param name="notifications">Notification repository.</param>
    /// <param name="sender">Notification sender (in-app in the demo).</param>
    public NotificationService(
        IRepository<Case> cases,
        IRepository<Notification> notifications,
        INotificationSender sender)
    {
        _cases = cases;
        _notifications = notifications;
        _sender = sender;
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
            .Include(c => c.CallLogs)
            .ToListAsync();
        var overdueCases = allCases
            .Where(c => OverduePolicy.NeedsFollowUp(c, now))
            .ToList();

        if (overdueCases.Count == 0)
        {
            return 0;
        }

        // Case ids that already have a notification (read or unread) — never
        // re-notify the same case, even after the user marks it read.
        var alreadyNotified = await _notifications.Query()
            .Where(n => n.CaseId.HasValue)
            .Select(n => n.CaseId!.Value)
            .ToListAsync();
        var alreadySet = new HashSet<int>(alreadyNotified);

        var created = 0;
        foreach (var c in overdueCases)
        {
            if (alreadySet.Contains(c.Id))
            {
                continue;
            }

            var daysOverdue = OverduePolicy.DaysOverdue(c, now);
            var customerName = c.Customer?.Name ?? "a customer";
            var notification = new Notification
            {
                Title = "Overdue follow-up",
                Message = $"Case #{c.Id} \"{c.Subject}\" for {customerName} is {daysOverdue} day(s) overdue for a follow-up.",
                Channel = NotificationChannel.InApp,
                Status = NotificationStatus.Unread,
                CreatedAtUtc = now,
                Link = $"/cases/{c.Id}",
                CaseId = c.Id,
            };
            await _sender.SendAsync(notification);
            created++;
        }

        return created;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NotificationDto>> GetAllAsync()
    {
        var list = await _notifications.Query()
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => NotificationDto.FromEntity(n))
            .ToListAsync();
        return list;
    }

    /// <inheritdoc/>
    public async Task<NotificationSummaryDto> GetSummaryAsync()
    {
        var unread = await _notifications.Query()
            .Where(n => n.Status == NotificationStatus.Unread)
            .ToListAsync();
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
