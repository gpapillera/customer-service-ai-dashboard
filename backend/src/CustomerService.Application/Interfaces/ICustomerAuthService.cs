using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for customer (portal) authentication.</summary>
public interface ICustomerAuthService
{
    /// <summary>Issues a fresh invite for a customer and emails the link.</summary>
    /// <param name="customerId">Customer id.</param>
    /// <returns>The generated invite token.</returns>
    Task<string> SendInviteAsync(int customerId);

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
}
