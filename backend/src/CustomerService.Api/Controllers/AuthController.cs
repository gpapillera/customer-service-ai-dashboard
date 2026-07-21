using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Authentication endpoints (JWT issuance).
/// See docs/DIY.md §4 for the login → token → interceptor → guard flow.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    /// <summary>Initializes a new <see cref="AuthController"/>.</summary>
    /// <param name="auth">Auth service.</param>
    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Authenticates a user and returns a JWT.</summary>
    /// <param name="request">Login credentials.</param>
    /// <returns>A <see cref="LoginResponse"/> with the token, or 401.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        return result is null ? Unauthorized() : Ok(result);
    }

    /// <summary>
    /// Public endpoint: validates a staff password-reset token and sets a new
    /// password. The same token-expiry/already-used validation pattern as the
    /// customer accept-invite flow.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var ok = await _auth.ResetPasswordAsync(request);
        if (!ok) return BadRequest(new { error = "This reset link is invalid, expired, or has already been used." });
        return Ok(new { message = "Password has been reset. You can now sign in." });
    }
}
