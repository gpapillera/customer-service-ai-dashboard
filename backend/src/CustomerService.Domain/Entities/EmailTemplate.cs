namespace CustomerService.Domain.Entities;

/// <summary>
/// An editable email template keyed by <see cref="Type"/> (the
/// <c>NotificationType</c> name, e.g. "CaseOverdue"). The subject and body
/// may contain personalization tokens such as <c>{{customerName}}</c> that are
/// substituted at send time from the related case/customer/agent. Replaces the
/// previously hard-coded text in <c>EmailNotificationSender.BuildContent</c>.
/// </summary>
public class EmailTemplate
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Template key — the <c>NotificationType</c> name this template serves
    /// (case-insensitive match). One template per type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Subject line. Supports <c>{{token}}</c> substitution.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Body text. Supports <c>{{token}}</c> substitution.</summary>
    public string Body { get; set; } = string.Empty;
}
