using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// CRUD endpoints for customers, plus name/email/phone search.
/// See docs/DIY.md §5 for the customer management walkthrough.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Agent")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _service;
    private readonly ICustomerAuthService _auth;

    /// <summary>Initializes a new <see cref="CustomersController"/>.</summary>
    /// <param name="service">Customer service.</param>
    /// <param name="auth">Customer auth service (invites).</param>
    public CustomersController(ICustomerService service, ICustomerAuthService auth)
    {
        _service = service;
        _auth = auth;
    }

    /// <summary>Lists all customers.</summary>
    /// <returns>All customers.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CustomerDto>> GetAll() => await _service.GetAllAsync();

    /// <summary>Gets a customer by id.</summary>
    /// <param name="id">Customer id.</param>
    /// <returns>The customer or 404.</returns>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(int id)
        => await _service.GetByIdAsync(id) is { } c ? Ok(c) : NotFound();

    /// <summary>Searches customers by name/email/phone.</summary>
    /// <param name="term">Search term.</param>
    /// <returns>Matching customers.</returns>
    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CustomerDto>> Search([FromQuery] string? term)
        => await _service.SearchAsync(term);

    /// <summary>Creates a customer.</summary>
    /// <param name="dto">Create payload.</param>
    /// <returns>The created customer.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Updates a customer.</summary>
    /// <param name="id">Customer id.</param>
    /// <param name="dto">Update payload.</param>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
    {
        if (id != dto.Id) return BadRequest();
        await _service.UpdateAsync(dto);
        return NoContent();
    }

    /// <summary>Deletes a customer.</summary>
    /// <param name="id">Customer id.</param>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Sends a customer-portal invite email. Both Admins and Agents may
    /// trigger this (business decision). Generates a fresh invite token,
    /// overwriting any unused prior invite, and emails the activation link.
    /// </summary>
    /// <param name="id">Customer id.</param>
    /// <returns>204 No Content on success, 400 if the customer has no email, 404 if not found.</returns>
    [HttpPost("{id:int}/invite")]
    [Authorize(Roles = "Admin,Agent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendInvite(int id)
    {
        try
        {
            await _auth.SendInviteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
