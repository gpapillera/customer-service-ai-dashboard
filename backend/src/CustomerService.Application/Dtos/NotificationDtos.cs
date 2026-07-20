using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>Read model for a notification shown in the in-app center.</summary>
public class NotificationDto
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Short title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Message body.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Delivery channel (InApp in the demo).</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Read/unread state.</summary>
    public NotificationStatus Status { get; set; }

    /// <summary>Why the notification was generated (overdue, resolved, etc.).</summary>
    public NotificationType Type { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Optional deep-link route (e.g. "/cases/6").</summary>
    public string? Link { get; set; }

    /// <summary>Related case id, when applicable.</summary>
    public int? CaseId { get; set; }

    /// <summary>Recipient address for Email/SMS channels (null for InApp).</summary>
    public string? Recipient { get; set; }

    /// <summary>Maps a <see cref="Notification"/> entity to its DTO.</summary>
    /// <param name="n">Source entity.</param>
    /// <returns>The DTO.</returns>
    public static NotificationDto FromEntity(Notification n) => new()
    {
        Id = n.Id,
        Title = n.Title,
        Message = n.Message,
        Channel = n.Channel,
        Status = n.Status,
        Type = n.Type,
        CreatedAtUtc = n.CreatedAtUtc,
        Link = n.Link,
        CaseId = n.CaseId,
        Recipient = n.Recipient,
    };
}

/// <summary>Summary of unread notifications for the bell badge.</summary>
public class NotificationSummaryDto
{
    /// <summary>Total number of unread notifications.</summary>
    public int UnreadCount { get; set; }

    /// <summary>Most recent notifications (for the dropdown preview).</summary>
    public List<NotificationDto> Recent { get; set; } = new();
}
