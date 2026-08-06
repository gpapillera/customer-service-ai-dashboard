namespace CustomerService.Domain.Entities;

/// <summary>
/// Singleton email-sending configuration. Exactly one row is expected
/// (Id = 1). Holds the "test" address that outbound emails are redirected to
/// when the recipient's domain is not on the allowed <see cref="EmailDomain"/>
/// list. This lets the demo verify delivery without spamming real customers.
/// </summary>
public class EmailConfig
{
    /// <summary>Primary key (fixed to 1 for the singleton row).</summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Address that non-listed-domain emails are redirected to. Defaults to
    /// the demo operator's test inbox. Admin-editable from the email
    /// configuration panel.
    /// </summary>
    public string TestEmailAddress { get; set; } = "glnppllr@gmail.com";
}
