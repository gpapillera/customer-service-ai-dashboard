using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// Manages the email configuration singleton, the allowed-domain list, and the
/// per-type email templates. All mutating operations are intended to be called
/// from Admin-only endpoints. The data backs recipient-domain routing
/// (listed domain → real recipient; otherwise → test address) and template
/// rendering in <c>EmailNotificationSender</c>.
/// </summary>
public interface IEmailConfigService
{
    /// <summary>Returns the config bundle (config + domains + templates + suggestions).</summary>
    Task<EmailConfigBundleDto> GetBundleAsync();

    /// <summary>Returns just the singleton config (test email).</summary>
    Task<EmailConfigDto> GetConfigAsync();

    /// <summary>Updates the test email address. Throws on invalid input.</summary>
    Task<EmailConfigDto> UpdateTestEmailAsync(string testEmail);

    /// <summary>Lists all allowed domains.</summary>
    Task<IReadOnlyList<EmailDomainDto>> ListDomainsAsync();

    /// <summary>Adds a domain (trimmed/lower-cased). Throws on blank or duplicate.</summary>
    Task<EmailDomainDto> AddDomainAsync(string domain, string? description);

    /// <summary>Updates an existing domain's value/description.</summary>
    Task<EmailDomainDto> UpdateDomainAsync(int id, string domain, string? description);

    /// <summary>Removes a domain. Returns false if not found.</summary>
    Task<bool> RemoveDomainAsync(int id);

    /// <summary>Lists all templates.</summary>
    Task<IReadOnlyList<EmailTemplateDto>> ListTemplatesAsync();

    /// <summary>
    /// Inserts or updates the template for <paramref name="type"/> (keyed
    /// case-insensitively). Returns the persisted row.
    /// </summary>
    Task<EmailTemplateDto> UpsertTemplateAsync(string type, string subject, string body);

    /// <summary>Removes a template. Returns false if not found.</summary>
    Task<bool> DeleteTemplateAsync(int id);
}
