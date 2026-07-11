using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for customer operations.</summary>
public interface ICustomerService
{
    /// <summary>Returns all customers (with case counts).</summary>
    /// <returns>List of <see cref="CustomerDto"/>.</returns>
    Task<IReadOnlyList<CustomerDto>> GetAllAsync();

    /// <summary>Returns a single customer by id.</summary>
    /// <param name="id">Customer id.</param>
    /// <returns>The <see cref="CustomerDto"/> or null.</returns>
    Task<CustomerDto?> GetByIdAsync(int id);

    /// <summary>Searches customers by name/email/phone substring.</summary>
    /// <param name="term">Search term (case-insensitive).</param>
    /// <returns>Matching customers.</returns>
    Task<IReadOnlyList<CustomerDto>> SearchAsync(string? term);

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
