using System.ComponentModel.DataAnnotations;

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
    /// <summary>User id (matches the JWT NameIdentifier claim; used for assignment checks).</summary>
    public string Id { get; set; } = string.Empty;

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

/// <summary>
/// Body for public customer self-registration (signup). No password field —
/// the customer sets one later via the emailed invite link. Only the profile
/// fields the customer is allowed to supply are present.
/// </summary>
public class RegisterCustomerDto
{
    /// <summary>Customer full name.</summary>
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Email address (used as the login identity).</summary>
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email is required.")]
    [StringLength(200, ErrorMessage = "Email must be 200 characters or fewer.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional phone number.</summary>
    [StringLength(30, ErrorMessage = "Phone must be 30 characters or fewer.")]
    public string? Phone { get; set; }

    /// <summary>Optional company name.</summary>
    [StringLength(150, ErrorMessage = "Company must be 150 characters or fewer.")]
    public string? Company { get; set; }

    /// <summary>Optional address.</summary>
    public string? Address { get; set; }
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

/// <summary>
/// Read model for the signed-in customer's own profile. Email is intentionally
/// read-only on the client (it is the login identity); only the editable
/// profile fields are returned here.
/// </summary>
public class CustomerProfileDto
{
    /// <summary>Customer id.</summary>
    public int Id { get; set; }

    /// <summary>Customer full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Login identity (read-only).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Optional company name.</summary>
    public string? Company { get; set; }

    /// <summary>Optional address.</summary>
    public string? Address { get; set; }
}

/// <summary>
/// Body for updating the signed-in customer's own profile. Email is NOT
/// accepted here — the customer id is taken strictly from the JWT claim by the
/// controller, and the email (login identity) is never editable.
/// </summary>
public class UpdateCustomerProfileDto
{
    /// <summary>Customer full name.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional phone number.</summary>
    [StringLength(30, ErrorMessage = "Phone must be 30 characters or fewer.")]
    public string? Phone { get; set; }

    /// <summary>Optional company name.</summary>
    [StringLength(150, ErrorMessage = "Company must be 150 characters or fewer.")]
    public string? Company { get; set; }

    /// <summary>Optional address.</summary>
    public string? Address { get; set; }
}

// ── Staff profile DTOs (Phase 10) ──

/// <summary>
/// Read model for the signed-in staff member's own profile. Email is intentionally
/// read-only (it is the login identity); only the editable profile fields are
/// returned here.
/// </summary>
public class StaffProfileDto
{
    /// <summary>User id (GUID string).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name shown in the UI.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Login identity (read-only).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Login username (read-only).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Staff role (Agent or Admin).</summary>
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Body for updating the signed-in staff member's own profile. Email is NOT
/// accepted — the user id is taken strictly from the JWT claim, and the email
/// (login identity) is never editable.
/// </summary>
public class UpdateStaffProfileDto
{
    /// <summary>Display name.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    public string FullName { get; set; } = string.Empty;
}

/// <summary>
/// Body for the public staff password-reset endpoint. Validates the token
/// and sets a new password in a single step.
/// </summary>
public class ResetPasswordRequest
{
    /// <summary>The reset token from the email link.</summary>
    [Required(ErrorMessage = "Token is required.")]
    public string Token { get; set; } = string.Empty;

    /// <summary>The new password the user chooses.</summary>
    [Required(ErrorMessage = "Password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; set; } = string.Empty;
}
