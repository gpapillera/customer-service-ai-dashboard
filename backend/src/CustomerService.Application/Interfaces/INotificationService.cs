using CustomerService.Application.Dtos;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// Generates and serves notifications (currently overdue follow-up alerts).
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Scans for overdue follow-ups and creates one in-app notification per
    /// overdue case that does not already have an unread notification. Safe to
    /// call repeatedly (idempotent).
    /// </summary>
    /// <returns>The number of new notifications created.</returns>
    Task<int> GenerateOverdueAsync();

    /// <summary>
    /// Sends a resolved/closed confirmation to the customer (Email channel
    /// only) when enabled. Skipped when the customer has no email. Idempotent
    /// on (CaseId, Email, CaseResolved). Safe to call from the status-update
    /// path; a delivery failure never blocks the status change.
    /// </summary>
    /// <param name="caseEntity">The case that was just resolved/closed.</param>
    /// <returns>The number of notifications created (0 or 1).</returns>
    Task<int> NotifyResolvedAsync(Case caseEntity);

    /// <summary>
    /// Notifies the agent assigned to a case that the customer posted a new
    /// message. Creates an in-app notification addressed to the assigned
    /// agent (Recipient = agent User.Id). If the case is unassigned, no
    /// notification is created and the caller is expected to log that there
    /// was no recipient. Idempotent on (CaseId, InApp, NewCustomerMessage) so
    /// repeated posts do not stack duplicate alerts for the same case.
    /// </summary>
    /// <param name="caseEntity">The case the customer commented on.</param>
    /// <param name="customerName">The customer's display name (for the message).</param>
    /// <returns>The number of notifications created (0 or 1).</returns>
    Task<int> NotifyNewCustomerMessageAsync(Case caseEntity, string customerName);

    /// <summary>Returns the email notification log (Channel == Email), newest first.</summary>
    /// <returns>The email log list.</returns>
    Task<IReadOnlyList<NotificationDto>> GetEmailLogAsync();

    /// <summary>Returns all notifications, newest first.</summary>
    /// <param name="recipientUserId">Optional user ID to filter notifications. When null (Admin), all notifications are returned. When set (Agent), only notifications addressed to this user or broadcast notifications (Recipient null) are returned.</param>
    /// <returns>The notification list.</returns>
    Task<IReadOnlyList<NotificationDto>> GetAllAsync(string? recipientUserId = null);

    /// <summary>Returns a summary (unread count + recent) for the bell.</summary>
    /// <param name="recipientUserId">Optional user ID to filter notifications. When null (Admin), all notifications are counted. When set (Agent), only notifications addressed to this user or broadcast notifications (Recipient null) are counted.</param>
    /// <returns>The summary.</returns>
    Task<NotificationSummaryDto> GetSummaryAsync(string? recipientUserId = null);

    /// <summary>Marks a notification as read.</summary>
    /// <param name="id">Notification id.</param>
    /// <returns>True if the notification existed and was updated.</returns>
    Task<bool> MarkReadAsync(int id);

    /// <summary>Marks every notification as read.</summary>
    /// <returns>The number of notifications updated.</returns>
    Task<int> MarkAllReadAsync();
}
