namespace CustomerService.Application.Options;

/// <summary>
/// SMTP / sender configuration for the real (MailKit) email sender. Bound from
/// the "Email" section of appsettings. Secrets (SenderPassword) live only in
/// appsettings.Development.json, which is git-ignored, so they never reach the
/// repo. See docs/DIY.md §7 for the notification flow.
/// </summary>
public class EmailOptions
{
    /// <summary>SMTP server host (e.g. smtp.gmail.com).</summary>
    public string SmtpHost { get; set; } = "smtp.gmail.com";

    /// <summary>SMTP port (587 for Gmail STARTTLS).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>From address used for outbound mail.</summary>
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>App-password / credential for <see cref="SenderEmail"/>.</summary>
    public string SenderPassword { get; set; } = string.Empty;

    /// <summary>Human-friendly From display name.</summary>
    public string SenderDisplayName { get; set; } = "Customer Service";

    /// <summary>
    /// When set AND the app is running in the Development environment, every
    /// outbound email is redirected to this address instead of the real
    /// recipient. The original intended recipient is preserved in the body and
    /// an "X-Original-Recipient" header so delivery is still verifiable. Leave
    /// empty to send to the real recipient in all environments.
    /// </summary>
    public string? DevOverrideRecipient { get; set; }
}
