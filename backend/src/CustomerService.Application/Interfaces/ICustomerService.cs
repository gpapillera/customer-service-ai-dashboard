using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for customer operations.</summary>
public interface ICustomerService
{
    /// <summary>Returns all customers (with case counts).</summary>
    /// <param name="callerRole">Role of the calling user (Admin sees all; Agent is scoped to customers who share at least one case with them).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>List of <see cref="CustomerDto"/>.</returns>
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(string? callerRole = null, string? callerUserId = null);

    /// <summary>Returns a single customer by id.</summary>
    /// <param name="id">Customer id.</param>
    /// <param name="callerRole">Role of the calling user (Admin sees all; Agent is blocked from customers they don't share a case with).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The <see cref="CustomerDto"/> or null.</returns>
    Task<CustomerDto?> GetByIdAsync(int id, string? callerRole = null, string? callerUserId = null);

    /// <summary>Searches customers by name/email/phone substring.</summary>
    /// <param name="term">Search term (case-insensitive).</param>
    /// <param name="callerRole">Role of the calling user (Admin sees all; Agent is scoped to customers who share at least one case with them).</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>Matching customers.</returns>
    Task<IReadOnlyList<CustomerDto>> SearchAsync(string? term, string? callerRole = null, string? callerUserId = null);

    /// <summary>Returns a customer's case history, scoped to the caller (an Agent only sees cases assigned to them).</summary>
    /// <param name="customerId">Customer id.</param>
    /// <param name="callerRole">Role of the calling user.</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The customer's cases visible to the caller.</returns>
    Task<IReadOnlyList<CaseDto>> GetCustomerCaseHistoryAsync(int customerId, string? callerRole = null, string? callerUserId = null);

    /// <summary>Creates a customer.</summary>
    /// <param name="dto">Create payload.</param>
    /// <returns>The created <see cref="CustomerDto"/>.</returns>
    Task<CustomerDto> CreateAsync(CreateCustomerDto dto);

    /// <summary>Updates a customer.</summary>
    /// <param name="dto">Update payload (must include id).</param>
    Task UpdateAsync(UpdateCustomerDto dto);

    /// <summary>Deletes a customer.</summary>
    /// <param name="id">Customer id.</param>
    Task DeleteAsync(int id);
}
