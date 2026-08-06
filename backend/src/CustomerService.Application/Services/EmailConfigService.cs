using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="IEmailConfigService"/> over the generic repositories.
/// Manages the singleton <see cref="EmailConfig"/>, the allowed
/// <see cref="EmailDomain"/> list, and editable <see cref="EmailTemplate"/> rows.
/// Mutating calls are expected to be guarded by Admin-only endpoints.
/// </summary>
public class EmailConfigService : IEmailConfigService
{
    /// <summary>Well-known consumer domains offered as quick-add chips in the UI.</summary>
    public static readonly IReadOnlyList<string> KnownDomainSuggestions = new[]
    {
        "gmail.com", "yahoo.com", "outlook.com", "hotmail.com",
        "icloud.com", "proton.me", "aol.com", "live.com",
    };

    private readonly IRepository<EmailConfig> _configs;
    private readonly IRepository<EmailDomain> _domains;
    private readonly IRepository<EmailTemplate> _templates;

    public EmailConfigService(
        IRepository<EmailConfig> configs,
        IRepository<EmailDomain> domains,
        IRepository<EmailTemplate> templates)
    {
        _configs = configs;
        _domains = domains;
        _templates = templates;
    }

    /// <inheritdoc/>
    public async Task<EmailConfigBundleDto> GetBundleAsync()
    {
        var config = await GetConfigAsync();
        var domains = await ListDomainsAsync();
        var templates = await ListTemplatesAsync();
        return new EmailConfigBundleDto
        {
            Config = config,
            Domains = domains,
            Templates = templates,
            KnownDomainSuggestions = KnownDomainSuggestions,
        };
    }

    /// <inheritdoc/>
    public async Task<EmailConfigDto> GetConfigAsync()
    {
        var row = await _configs.Query().FirstOrDefaultAsync(c => c.Id == 1);
        if (row is null)
        {
            // Ensure the singleton exists with the default test address.
            row = new EmailConfig { Id = 1, TestEmailAddress = "glnppllr@gmail.com" };
            await _configs.AddAsync(row);
            await _configs.SaveChangesAsync();
        }

        return new EmailConfigDto
        {
            Id = row.Id,
            TestEmailAddress = row.TestEmailAddress,
        };
    }

    /// <inheritdoc/>
    public async Task<EmailConfigDto> UpdateTestEmailAsync(string testEmail)
    {
        var normalized = (testEmail ?? string.Empty).Trim();
        if (!IsValidEmail(normalized))
            throw new ArgumentException("A valid test email address is required.", nameof(testEmail));

        var row = await _configs.Query().FirstOrDefaultAsync(c => c.Id == 1);
        if (row is null)
        {
            row = new EmailConfig { Id = 1 };
            await _configs.AddAsync(row);
        }

        row.TestEmailAddress = normalized;
        _configs.Update(row);
        await _configs.SaveChangesAsync();

        return new EmailConfigDto
        {
            Id = row.Id,
            TestEmailAddress = row.TestEmailAddress,
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EmailDomainDto>> ListDomainsAsync()
    {
        var list = await _domains.Query()
            .OrderBy(d => d.Domain)
            .Select(d => new EmailDomainDto { Id = d.Id, Domain = d.Domain, Description = d.Description })
            .ToListAsync();
        return list;
    }

    /// <inheritdoc/>
    public async Task<EmailDomainDto> AddDomainAsync(string domain, string? description)
    {
        var normalized = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Domain must not be empty.", nameof(domain));

        var duplicate = await _domains.Query()
            .AnyAsync(d => d.Domain == normalized);
        if (duplicate)
            throw new InvalidOperationException($"Domain '{normalized}' is already in the list.");

        var entity = new EmailDomain { Domain = normalized, Description = description?.Trim() };
        await _domains.AddAsync(entity);
        await _domains.SaveChangesAsync();

        return new EmailDomainDto
        {
            Id = entity.Id,
            Domain = entity.Domain,
            Description = entity.Description,
        };
    }

    /// <inheritdoc/>
    public async Task<EmailDomainDto> UpdateDomainAsync(int id, string domain, string? description)
    {
        var entity = await _domains.GetByIdAsync(id);
        if (entity is null)
            throw new KeyNotFoundException($"Domain #{id} not found.");

        var normalized = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Domain must not be empty.", nameof(domain));

        var duplicate = await _domains.Query()
            .AnyAsync(d => d.Domain == normalized && d.Id != id);
        if (duplicate)
            throw new InvalidOperationException($"Domain '{normalized}' is already in the list.");

        entity.Domain = normalized;
        entity.Description = description?.Trim();
        _domains.Update(entity);
        await _domains.SaveChangesAsync();

        return new EmailDomainDto
        {
            Id = entity.Id,
            Domain = entity.Domain,
            Description = entity.Description,
        };
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveDomainAsync(int id)
    {
        var entity = await _domains.GetByIdAsync(id);
        if (entity is null)
            return false;

        _domains.Remove(entity);
        await _domains.SaveChangesAsync();
        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EmailTemplateDto>> ListTemplatesAsync()
    {
        var list = await _templates.Query()
            .OrderBy(t => t.Type)
            .Select(t => new EmailTemplateDto { Id = t.Id, Type = t.Type, Subject = t.Subject, Body = t.Body })
            .ToListAsync();
        return list;
    }

    /// <inheritdoc/>
    public async Task<EmailTemplateDto> UpsertTemplateAsync(string type, string subject, string body)
    {
        var key = (type ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Template type must not be empty.", nameof(type));

        var existing = await _templates.Query()
            .FirstOrDefaultAsync(t => t.Type == key);
        if (existing is null)
        {
            existing = new EmailTemplate { Type = key };
            await _templates.AddAsync(existing);
        }

        existing.Subject = subject ?? string.Empty;
        existing.Body = body ?? string.Empty;
        _templates.Update(existing);
        await _templates.SaveChangesAsync();

        return new EmailTemplateDto
        {
            Id = existing.Id,
            Type = existing.Type,
            Subject = existing.Subject,
            Body = existing.Body,
        };
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteTemplateAsync(int id)
    {
        var entity = await _templates.GetByIdAsync(id);
        if (entity is null)
            return false;

        _templates.Remove(entity);
        await _templates.SaveChangesAsync();
        return true;
    }

    /// <summary>Lower-cases and strips a leading '@' or 'www.'/'http' prefix noise.</summary>
    internal static string NormalizeDomain(string domain)
    {
        var d = (domain ?? string.Empty).Trim().ToLowerInvariant();
        d = d.TrimStart('@');
        if (d.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            d = d["http://".Length..];
        if (d.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            d = d["https://".Length..];
        if (d.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            d = d["www.".Length..];
        var slash = d.IndexOf('/');
        if (slash >= 0)
            d = d[..slash];
        return d;
    }

    /// <summary>Minimal email-shape check (non-empty, has exactly one '@', domain part present).</summary>
    internal static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@'))
            return false;
        var domain = email[(at + 1)..];
        return domain.Length > 0 && !domain.Contains(' ') && domain.Contains('.');
    }
}
