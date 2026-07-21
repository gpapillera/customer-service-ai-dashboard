using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;

namespace CustomerService.Tests.Fakes;

/// <summary>
/// Minimal in-memory <see cref="ICustomerAuthService"/> for controller tests.
/// Only the members exercised by <see cref="CustomerPortalController"/> are
/// implemented; the rest throw to surface unexpected calls.
/// </summary>
public class FakeCustomerAuthService : ICustomerAuthService
{
    public Task<string> SendInviteAsync(int customerId) =>
        throw new System.NotImplementedException();

    public Task RegisterAsync(RegisterCustomerDto dto) =>
        throw new System.NotImplementedException();

    public Task<ValidateInviteResponse> ValidateInviteAsync(string token) =>
        throw new System.NotImplementedException();

    public Task AcceptInviteAsync(AcceptInviteRequest request) =>
        throw new System.NotImplementedException();

    public Task<CustomerLoginResponse?> LoginAsync(CustomerLoginRequest request) =>
        throw new System.NotImplementedException();

    public Task<CustomerProfileDto> GetProfileAsync(int customerId) =>
        System.Threading.Tasks.Task.FromResult(new CustomerProfileDto
        {
            Id = customerId,
            Name = "Juan",
            Email = "juan@example.com",
        });

    public Task UpdateProfileAsync(int customerId, UpdateCustomerProfileDto dto) =>
        System.Threading.Tasks.Task.CompletedTask;

    public Task RequestPasswordResetAsync(int customerId) =>
        System.Threading.Tasks.Task.CompletedTask;
}
