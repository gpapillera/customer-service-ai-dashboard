namespace CustomerService.Application.Dtos;

/// <summary>Credentials submitted to the login endpoint.</summary>
public class LoginRequest
{
    /// <summary>Username.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Plaintext password (never stored).</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>JWT auth result returned on successful login.</summary>
public class LoginResponse
{
    /// <summary>JWT bearer token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Token expiry (UTC).</summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>Username.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Assigned role.</summary>
    public string Role { get; set; } = string.Empty;
}
