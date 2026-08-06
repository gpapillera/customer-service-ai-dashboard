namespace CustomerService.Domain.Entities;

/// <summary>
/// An allowed email domain. When an outbound email's recipient domain is in
/// this list, the message is delivered to the real recipient. Otherwise it is
/// redirected to <see cref="EmailConfig.TestEmailAddress"/>. Lets admins
/// decide which domains are safe/real enough to receive mail directly.
/// </summary>
public class EmailDomain
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Domain suffix, stored lower-cased without the leading dot (e.g.
    /// "gmail.com"). Matched against the part after the final '@' of a
    /// recipient address.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Optional human note (e.g. "Consumer GMail").</summary>
    public string? Description { get; set; }
}
