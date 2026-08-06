using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Admin-only management of email configuration: the test/delivery address,
/// the allowed-domain list that controls direct delivery, and the per-type
/// email templates with personalization tokens. See docs/DIY.md §7.
/// </summary>
[ApiController]
[Route("api/email-config")]
[Authorize(Roles = "Admin")]
public class EmailConfigController : ControllerBase
{
    private readonly IEmailConfigService _service;

    public EmailConfigController(IEmailConfigService service)
    {
        _service = service;
    }

    /// <summary>Returns the full config bundle (config + domains + templates + suggestions).</summary>
    [HttpGet]
    public async Task<EmailConfigBundleDto> Get() => await _service.GetBundleAsync();

    /// <summary>Updates the test/delivery email address.</summary>
    [HttpPut("test-email")]
    public async Task<IActionResult> UpdateTestEmail([FromBody] EmailConfigTestEmailRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TestEmail))
            return BadRequest("Test email is required.");
        try
        {
            var dto = await _service.UpdateTestEmailAsync(request.TestEmail);
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Lists allowed domains.</summary>
    [HttpGet("domains")]
    public async Task<IReadOnlyList<EmailDomainDto>> ListDomains() => await _service.ListDomainsAsync();

    /// <summary>Adds an allowed domain.</summary>
    [HttpPost("domains")]
    public async Task<IActionResult> AddDomain([FromBody] EmailConfigDomainRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Domain))
            return BadRequest("Domain is required.");
        try
        {
            var dto = await _service.AddDomainAsync(request.Domain, request.Description);
            return Ok(dto);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Updates an allowed domain.</summary>
    [HttpPut("domains/{id:int}")]
    public async Task<IActionResult> UpdateDomain(int id, [FromBody] EmailConfigDomainRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Domain))
            return BadRequest("Domain is required.");
        try
        {
            var dto = await _service.UpdateDomainAsync(id, request.Domain, request.Description);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Removes an allowed domain.</summary>
    [HttpDelete("domains/{id:int}")]
    public async Task<IActionResult> RemoveDomain(int id)
    {
        var ok = await _service.RemoveDomainAsync(id);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Lists templates.</summary>
    [HttpGet("templates")]
    public async Task<IReadOnlyList<EmailTemplateDto>> ListTemplates() => await _service.ListTemplatesAsync();

    /// <summary>Inserts or updates a template for a type.</summary>
    [HttpPost("templates")]
    public async Task<IActionResult> UpsertTemplate([FromBody] EmailConfigTemplateRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Type))
            return BadRequest("Template type is required.");
        var dto = await _service.UpsertTemplateAsync(request.Type, request.Subject ?? string.Empty, request.Body ?? string.Empty);
        return Ok(dto);
    }

    /// <summary>Removes a template.</summary>
    [HttpDelete("templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        var ok = await _service.DeleteTemplateAsync(id);
        return ok ? NoContent() : NotFound();
    }
}

/// <summary>Request body for updating the test email.</summary>
public record EmailConfigTestEmailRequest(string TestEmail);

/// <summary>Request body for adding/updating a domain.</summary>
public record EmailConfigDomainRequest(string Domain, string? Description = null);

/// <summary>Request body for upserting a template.</summary>
public record EmailConfigTemplateRequest(string Type, string? Subject = null, string? Body = null);
