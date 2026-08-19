using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace CustomerService.Api.Services;

/// <summary>
/// Builds the HttpOnly cookie options used for the access + refresh tokens, and
/// writes/clears them on the response. Centralized so the four auth endpoints
/// (staff login, staff refresh, customer login, customer refresh) stay
/// consistent. Cookies are NEVER readable by JavaScript (HttpOnly) so an XSS
/// payload cannot exfiltrate the token.
///
/// The Secure flag follows the request scheme: HTTPS → Secure (prod), HTTP →
/// not Secure (dev over the Angular proxy). Tying it to the env var would
/// wrongly emit Secure on a plaintext dev connection and the browser would
/// silently drop the cookie, breaking login in development.
/// </summary>
public interface ITokenCookieService
{
    /// <summary>Appends the access_token + refresh_token cookies to the response.</summary>
    void AppendAuthCookies(HttpResponse response, string accessToken, string refreshToken);

    /// <summary>Clears both auth cookies (used on logout).</summary>
    void ClearAuthCookies(HttpResponse response);
}

public class TokenCookieService : ITokenCookieService
{
    private readonly IConfiguration _config;

    public TokenCookieService(IConfiguration config)
    {
        _config = config;
    }

    public void AppendAuthCookies(HttpResponse response, string accessToken, string refreshToken)
    {
        var accessMinutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var m) ? m : 15;
        var refreshDays = int.TryParse(_config["Jwt:RefreshTokenDays"], out var d) ? d : 14;
        // Secure only when the transport is TLS — never on a plaintext dev connection.
        var secure = response.HttpContext.Request.IsHttps;

        response.Cookies.Append("access_token", accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTime.UtcNow.AddMinutes(accessMinutes),
        });
        response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTime.UtcNow.AddDays(refreshDays),
        });
    }

    public void ClearAuthCookies(HttpResponse response)
    {
        response.Cookies.Delete("access_token", new CookieOptions { Path = "/" });
        response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/" });
    }
}
