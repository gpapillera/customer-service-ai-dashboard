using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using CustomerService.Api;
using CustomerService.Application.Dtos;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace CustomerService.Tests;

/// <summary>
/// Regression tests for the backend security hardening pass. These prove the
/// production-safety invariants hold, not just that the code compiles.
/// </summary>
public class SecurityHardeningTests
{
    private readonly ITestOutputHelper _output;
    public SecurityHardeningTests(ITestOutputHelper output) => _output = output;

    // Privilege-bearing fields a public caller must never be able to set.
    private static readonly HashSet<string> PrivilegedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Role", "IsActive", "PasswordHash", "Id",
    };

    [Fact]
    public void JwtKeyGuard_FailsFast_WhenKeyIsDefault()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:Key", "dev-insecure-key-change-me-1234567890"));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public async Task AuthEndpoints_RateLimited_AfterFiveAttempts()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:Key", "test-key-not-the-real-one-0123456789abcdef");
                b.UseSetting("AllowedHosts", "*");
            });
        var client = factory.CreateClient();
        var payload = new StringContent("{\"userName\":\"x\",\"password\":\"y\"}", Encoding.UTF8, "application/json");

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var resp = await client.PostAsync("/api/auth/login", payload);
            codes.Add(resp.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, codes[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[5]);
    }

    [Fact]
    public async Task SecurityHeaders_Present_OnResponses()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:Key", "test-key-not-the-real-one-0123456789abcdef");
                b.UseSetting("AllowedHosts", "*");
            });
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/customer-auth/validate-invite");

        Assert.True(resp.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").First());
        Assert.True(resp.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").First());
    }

    [Fact]
    public void PublicRequestDtos_HaveNoPrivilegedSettableFields()
    {
        var publicRequestTypes = new[]
        {
            typeof(LoginRequest),
            typeof(ResetPasswordRequest),
            typeof(RegisterCustomerDto),
            typeof(AcceptInviteRequest),
            typeof(CustomerLoginRequest),
        };

        foreach (var type in publicRequestTypes)
        {
            var badFields = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && PrivilegedFields.Contains(p.Name))
                .Select(p => p.Name)
                .ToList();

            Assert.Empty(badFields);
        }
    }

    private static WebApplicationFactory<Program> CookieFactory()
    {
        // Use a unique on-disk SQLite file per factory so parallel/full-suite
        // runs don't share a database (which would let seeded refresh tokens
        // from other tests interfere with the revocation assertions).
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cs_auth_test_{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", "test-key-not-the-real-one-0123456789abcdef");
            b.UseSetting("AllowedHosts", "*");
            b.UseSetting("ConnectionStrings:Sqlite", $"Data Source={dbPath}");
        });
    }

    [Fact]
    public async Task Login_SetsHttpOnlyAccessAndRefreshCookies()
    {
        using var factory = CookieFactory();
        var client = factory.CreateClient();
        var payload = new StringContent("{\"userName\":\"admin\",\"password\":\"Passw0rd!\"}", Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/api/auth/login", payload);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var cookies = string.Join("; ", setCookies!);
        Assert.Contains("access_token=", cookies);
        Assert.Contains("refresh_token=", cookies);
        // HttpOnly must be present so JS/XSS cannot read the token (emitted lowercase on the wire).
        Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase);
        // SameSite must be set (lax is fine for same-site dev + prod subdomains).
        Assert.Contains("samesite", cookies, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        using var factory = CookieFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndRevokesPrevious()
    {
        using var factory = CookieFactory();
        var client = factory.CreateClient();

        // First login to get the cookies.
        var login = await client.PostAsync("/api/auth/login",
            new StringContent("{\"userName\":\"admin\",\"password\":\"Passw0rd!\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var loginCookies));

        // Re-issue the cookies on a fresh client (simulating the browser storing them).
        var refreshClient = factory.CreateClient();
        foreach (var c in loginCookies!)
        {
            var nameValue = c.Split(';')[0];
            var idx = nameValue.IndexOf('=');
            refreshClient.DefaultRequestHeaders.Add("Cookie", nameValue);
        }

        var firstRefresh = await refreshClient.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        Assert.True(firstRefresh.Headers.TryGetValues("Set-Cookie", out var rotatedCookies));
        var rotated = string.Join("; ", rotatedCookies!);
        Assert.Contains("refresh_token=", rotated);
        Assert.Contains("httponly", rotated, StringComparison.OrdinalIgnoreCase);

        // The OLD refresh cookie must now be revoked: replaying it yields 401.
        var oldRefresh = loginCookies.First(c => c.Contains("refresh_token="));
        var oldNameValue = oldRefresh.Split(';')[0];
        var replayClient = factory.CreateClient();
        replayClient.DefaultRequestHeaders.Add("Cookie", oldNameValue);
        var replay = await replayClient.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }
}
