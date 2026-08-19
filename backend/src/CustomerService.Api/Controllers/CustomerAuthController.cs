using CustomerService.Api.Services;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Public customer-portal authentication endpoints: validate invite, accept
/// invite, customer login, refresh, and logout. The invite/accept/register
/// flows are unauthenticated (the customer has no session yet). See docs/DIY.md §8.
/// </summary>
[ApiController]
[Route("api/customer-auth")]
public class CustomerAuthController : ControllerBase
{
    private readonly ICustomerAuthService _auth;
    private readonly ITokenCookieService _cookies;

    /// <summary>Initializes a new <see cref="CustomerAuthController"/>.</summary>
    /// <param name="auth">Customer auth service.</param>
    /// <param name="cookies">Cookie issuer for the access + refresh tokens.</param>
    public CustomerAuthController(ICustomerAuthService auth, ITokenCookieService cookies)
    {
        _auth = auth;
        _cookies = cookies;
    }

    /// <summary>
    /// Validates an invite token without requiring auth. Returns whether the
    /// token is valid plus the customer's display name / masked email so the
    /// future frontend can render "Set your password for [name]".
    /// </summary>
    [HttpGet("validate-invite")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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
    [HttpPost("accept-invite")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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
    /// Logs a customer in, sets the access + refresh cookies, and returns the
    /// JWT (role = "Customer"). Wrong password, inactive account, or unknown
    /// email all return the same generic error to avoid leaking which part failed.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] CustomerLoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        if (result is null)
        {
            return Unauthorized(new { error = "Invalid credentials." });
        }

        _cookies.AppendAuthCookies(Response, result.Token, result.RefreshToken!);
        return Ok(result);
    }

    /// <summary>
    /// Rotates the customer refresh cookie into a fresh access + refresh pair.
    /// Returned both as cookies and in the response body.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { error = "No refresh token." });
        }

        try
        {
            var (accessToken, newRefresh, expires) = await _auth.RefreshAsync(refreshToken);
            _cookies.AppendAuthCookies(Response, accessToken, newRefresh);
            return Ok(new CustomerRefreshResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefresh,
                ExpiresUtc = expires,
            });
        }
        catch (InvalidOperationException)
        {
            _cookies.ClearAuthCookies(Response);
            return Unauthorized(new { error = "Invalid or expired refresh token." });
        }
    }

    /// <summary>Clears the auth cookies and revokes the customer refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _auth.LogoutAsync(refreshToken);
        }

        _cookies.ClearAuthCookies(Response);
        return Ok(new { message = "Logged out." });
    }

    /// <summary>
    /// Public customer self-registration (signup). Creates a new customer
    /// record (no password is collected) and emails an activation link reusing
    /// the same invite logic as <c>POST /api/customers/{id}/invite</c>. No
    /// token/JWT is returned — the customer must click the emailed link and set
    /// a password before they can log in.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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
