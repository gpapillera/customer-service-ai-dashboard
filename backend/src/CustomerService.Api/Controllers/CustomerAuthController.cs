using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Public customer-portal authentication endpoints: validate invite, accept
/// invite, and customer login. These are unauthenticated (the customer has no
/// session yet). See docs/DIY.md §8.
/// </summary>
[ApiController]
[Route("api/customer-auth")]
public class CustomerAuthController : ControllerBase
{
    private readonly ICustomerAuthService _auth;

    /// <summary>Initializes a new <see cref="CustomerAuthController"/>.</summary>
    /// <param name="auth">Customer auth service.</param>
    public CustomerAuthController(ICustomerAuthService auth) => _auth = auth;

    /// <summary>
    /// Validates an invite token without requiring auth. Returns whether the
    /// token is valid plus the customer's display name / masked email so the
    /// future frontend can render "Set your password for [name]".
    /// </summary>
    /// <param name="token">Invite token.</param>
    /// <returns>A <see cref="ValidateInviteResponse"/>.</returns>
    [HttpGet("validate-invite")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ValidateInviteResponse>> ValidateInvite([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { error = "Token is required." });
        }
        return Ok(await _auth.ValidateInviteAsync(token));
    }

    /// <summary>
    /// Accepts an invite: validates the token, sets the password (BCrypt), and
    /// activates the account. Does not log the customer in.
    /// </summary>
    /// <param name="request">Token + new password.</param>
    /// <returns>204 No Content on success, 400 on invalid/expired/used token.</returns>
    [HttpPost("accept-invite")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Token and password are required." });
        }
        try
        {
            await _auth.AcceptInviteAsync(request);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Logs a customer in and returns a JWT (role = "Customer"). Wrong
    /// password, inactive account, or unknown email all return the same
    /// generic error to avoid leaking which part failed.
    /// </summary>
    /// <param name="request">Email + password.</param>
    /// <returns>A <see cref="CustomerLoginResponse"/> or 401.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] CustomerLoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        return result is null ? Unauthorized(new { error = "Invalid credentials." }) : Ok(result);
    }

    /// <summary>
    /// Public customer self-registration (signup). Creates a new customer
    /// record (no password is collected) and emails an activation link reusing
    /// the same invite logic as <c>POST /api/customers/{id}/invite</c>. No
    /// token/JWT is returned — the customer must click the emailed link and set
    /// a password before they can log in.
    /// </summary>
    /// <param name="request">Full name, email, phone, company, address.</param>
    /// <returns>204 No Content on success, 400 if the email is already in use or the payload is invalid.</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterCustomerDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        try
        {
            await _auth.RegisterAsync(request);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
