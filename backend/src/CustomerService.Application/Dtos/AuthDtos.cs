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

/// <summary>Result of validating a customer invite token (public).</summary>
public class ValidateInviteResponse
{
    /// <summary>Whether the token is valid (exists, unexpired, unused).</summary>
    public bool Valid { get; set; }

    /// <summary>Customer display name (for the "set your password" screen).</summary>
    public string? CustomerName { get; set; }

    /// <summary>Partially masked customer email (e.g. j***@acme.ph).</summary>
    public string? CustomerEmailMasked { get; set; }
}

/// <summary>Body for accepting a customer invite.</summary>
public class AcceptInviteRequest
{
    /// <summary>The invite token from the email link.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The new password the customer chooses.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Body for customer login (public).</summary>
public class CustomerLoginRequest
{
    /// <summary>Customer email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Plaintext password (never stored).</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>JWT auth result returned on successful customer login.</summary>
public class CustomerLoginResponse
{
    /// <summary>JWT bearer token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Token expiry (UTC).</summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>Customer id (claim subject).</summary>
    public int CustomerId { get; set; }

    /// <summary>Customer display name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Assigned role (always "Customer").</summary>
    public string Role { get; set; } = "Customer";
}
