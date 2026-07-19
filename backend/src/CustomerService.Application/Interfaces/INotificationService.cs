using CustomerService.Application.Dtos;

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

    /// <summary>Returns all notifications, newest first.</summary>
    /// <returns>The notification list.</returns>
    Task<IReadOnlyList<NotificationDto>> GetAllAsync();

    /// <summary>Returns a summary (unread count + recent) for the bell.</summary>
    /// <returns>The summary.</returns>
    Task<NotificationSummaryDto> GetSummaryAsync();

    /// <summary>Marks a notification as read.</summary>
    /// <param name="id">Notification id.</param>
    /// <returns>True if the notification existed and was updated.</returns>
    Task<bool> MarkReadAsync(int id);

    /// <summary>Marks every notification as read.</summary>
    /// <returns>The number of notifications updated.</returns>
    Task<int> MarkAllReadAsync();
}
