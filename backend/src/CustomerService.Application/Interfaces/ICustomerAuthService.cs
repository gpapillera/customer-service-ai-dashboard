using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for customer (portal) authentication.</summary>
public interface ICustomerAuthService
{
    /// <summary>Issues a fresh invite for a customer and emails the link.</summary>
    /// <param name="customerId">Customer id.</param>
    /// <returns>The generated invite token.</returns>
    Task<string> SendInviteAsync(int customerId);

    /// <summary>
    /// Self-service signup: creates a new customer (no password collected) and
    /// emails an activation link reusing the same invite logic as
    /// <see cref="SendInviteAsync"/>. Throws if the email is already in use.
    /// </summary>
    /// <param name="dto">Registration payload (name, email, phone, company, address).</param>
    Task RegisterAsync(RegisterCustomerDto dto);

    /// <summary>Validates an invite token without auth.</summary>
    /// <param name="token">Invite token.</param>
    /// <returns>A <see cref="ValidateInviteResponse"/>.</returns>
    Task<ValidateInviteResponse> ValidateInviteAsync(string token);

    /// <summary>Accepts an invite: sets the password and activates the account.</summary>
    /// <param name="request">Token + new password.</param>
    Task AcceptInviteAsync(AcceptInviteRequest request);

    /// <summary>Logs a customer in and returns a JWT (role = Customer).</summary>
    /// <param name="request">Email + password.</param>
    /// <returns>A <see cref="CustomerLoginResponse"/> or null if invalid.</returns>
    Task<CustomerLoginResponse?> LoginAsync(CustomerLoginRequest request);

    /// <summary>
    /// Returns the signed-in customer's own profile (read-only email + editable
    /// fields). The id is supplied by the caller (taken from the JWT claim by
    /// the controller) — never from a client body.
    /// </summary>
    /// <param name="customerId">Customer id (from the JWT claim).</param>
    Task<CustomerProfileDto> GetProfileAsync(int customerId);

    /// <summary>
    /// Updates the signed-in customer's own profile. Only the editable fields
    /// (name/phone/company/address) are changed; email and id are never
    /// touched. The id is supplied by the caller (from the JWT claim).
    /// </summary>
    /// <param name="customerId">Customer id (from the JWT claim).</param>
    /// <param name="dto">Editable profile fields.</param>
    Task UpdateProfileAsync(int customerId, UpdateCustomerProfileDto dto);

    /// <summary>
    /// Requests a password reset for the signed-in customer. Regenerates the
    /// SAME invite token / expiry fields already used by the invite flow and
    /// emails a reset link — reusing the existing accept-invite endpoint to
    /// actually set the new password. The id is supplied by the caller (from
    /// the JWT claim); no email lookup is needed since the customer is already
    /// authenticated.
    /// </summary>
    /// <param name="customerId">Customer id (from the JWT claim).</param>
    Task RequestPasswordResetAsync(int customerId);
}
