namespace CustomerService.Application.Dtos;

/// <summary>Read/upsert model for the singleton email configuration.</summary>
public class EmailConfigDto
{
    /// <summary>Primary key (always 1).</summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Address that non-listed-domain emails are redirected to. Admin-editable.
    /// </summary>
    public string TestEmailAddress { get; set; } = string.Empty;
}

/// <summary>Read/upsert model for an allowed email domain.</summary>
public class EmailDomainDto
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Domain suffix, lower-cased (e.g. "gmail.com").</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Optional human note.</summary>
    public string? Description { get; set; }
}

/// <summary>Read/upsert model for an editable email template.</summary>
public class EmailTemplateDto
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Template key — the NotificationType name (e.g. "CaseOverdue").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Subject line. Supports {{token}} substitution.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Body text. Supports {{token}} substitution.</summary>
    public string Body { get; set; } = string.Empty;
}

/// <summary>
/// Bundle returned to the config UI so it can render everything in one call.
/// </summary>
public class EmailConfigBundleDto
{
    /// <summary>The singleton config (test email).</summary>
    public EmailConfigDto Config { get; set; } = new();

    /// <summary>Allowed domains.</summary>
    public IReadOnlyList<EmailDomainDto> Domains { get; set; } = Array.Empty<EmailDomainDto>();

    /// <summary>Editable templates, one per NotificationType.</summary>
    public IReadOnlyList<EmailTemplateDto> Templates { get; set; } = Array.Empty<EmailTemplateDto>();

    /// <summary>Well-known domains offered as quick-add chips in the UI.</summary>
    public IReadOnlyList<string> KnownDomainSuggestions { get; set; } = Array.Empty<string>();
}
