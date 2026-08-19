using CustomerService.Api.Services;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Authentication endpoints (JWT issuance + refresh + logout).
/// See docs/DIY.md §4 for the login → cookie → interceptor → guard flow.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ITokenCookieService _cookies;

    /// <summary>Initializes a new <see cref="AuthController"/>.</summary>
    /// <param name="auth">Auth service.</param>
    /// <param name="cookies">Cookie issuer for the access + refresh tokens.</param>
    public AuthController(IAuthService auth, ITokenCookieService cookies)
    {
        _auth = auth;
        _cookies = cookies;
    }

    /// <summary>Authenticates a user and sets the access + refresh cookies.</summary>
    /// <param name="request">Login credentials.</param>
    /// <returns>A <see cref="LoginResponse"/> (token also in cookies), or 401.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
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
    /// Rotates the refresh cookie into a fresh access + refresh pair. The new
    /// tokens are returned both as cookies and in the response body.
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
            return Ok(new RefreshResponse
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

    /// <summary>Clears the auth cookies and revokes the refresh token.</summary>
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
    /// Public endpoint: validates a staff password-reset token and sets a new
    /// password. The same token-expiry/already-used validation pattern as the
    /// customer accept-invite flow.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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
