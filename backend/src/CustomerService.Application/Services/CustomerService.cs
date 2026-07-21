using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="ICustomerService"/> using repositories.
/// </summary>
public class CustomerService : ICustomerService
{
    private readonly IRepository<Customer> _customers;
    private readonly IRepository<Case> _cases;

    /// <summary>Initializes a new <see cref="CustomerService"/>.</summary>
    /// <param name="customers">Customer repository.</param>
    /// <param name="cases">Case repository (for counts).</param>
    public CustomerService(IRepository<Customer> customers, IRepository<Case> cases)
    {
        _customers = customers;
        _cases = cases;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(string? callerRole = null, string? callerUserId = null)
    {
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);

        // SERVER-SIDE AGENT SCOPING (Phase 6). An Agent only sees customers who
        // have at least one case assigned to them (join/exists query, not
        // client-side filtering). Admin is unaffected.
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var customerIds = await _cases.Query()
                .Where(c => c.AssignedToUserId == callerUserId)
                .Select(c => c.CustomerId)
                .Distinct()
                .ToListAsync();

            return await _customers.Query()
                .Where(c => customerIds.Contains(c.Id))
                .Select(c => new CustomerDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Email = c.Email,
                    Phone = c.Phone,
                    Company = c.Company,
                    Address = c.Address,
                    CaseCount = c.Cases.Count,
                    CreatedAtUtc = c.CreatedAtUtc,
                })
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        return await _customers.Query()
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                Company = c.Company,
                Address = c.Address,
                CaseCount = c.Cases.Count,
                CreatedAtUtc = c.CreatedAtUtc,
            })
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<CustomerDto?> GetByIdAsync(int id, string? callerRole = null, string? callerUserId = null)
    {
        var c = await _customers.GetByIdAsync(id);
        if (c is null) return null;

        // AGENT SCOPING (Phase 6). An Agent may only open a customer they share
        // at least one case with. Admin is unaffected.
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var sharesCase = await _cases.Query()
                .AnyAsync(x => x.CustomerId == id && x.AssignedToUserId == callerUserId);
            if (!sharesCase)
            {
                throw new ForbiddenException("You can only view customers you share a case with.");
            }
        }

        return ToDto(c);
    }

    /// <summary>
    /// Returns the case history for a customer, scoped for an Agent caller to
    /// only the cases assigned to them. Admin sees the full history. Used by
    /// the customer detail endpoint so an Agent never sees another agent's
    /// cases with the same customer.
    /// </summary>
    /// <param name="customerId">Customer id.</param>
    /// <param name="callerRole">Role of the calling user.</param>
    /// <param name="callerUserId">Id of the calling user (used to scope an Agent's view).</param>
    /// <returns>The customer's cases visible to the caller.</returns>
    public async Task<IReadOnlyList<CaseDto>> GetCustomerCaseHistoryAsync(int customerId, string? callerRole = null, string? callerUserId = null)
    {
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);
        IQueryable<Case> q = _cases.Query()
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .Where(c => c.CustomerId == customerId);
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            q = q.Where(c => c.AssignedToUserId == callerUserId);
        }
        return await q.OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => CaseService.ToDto(c))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomerDto>> SearchAsync(string? term, string? callerRole = null, string? callerUserId = null)
    {
        var isAgent = string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase);

        // SERVER-SIDE AGENT SCOPING (Phase 6): same rule as GetAllAsync — an
        // Agent only searches within customers who share a case with them.
        IQueryable<Customer> q = _customers.Query();
        if (isAgent && !string.IsNullOrEmpty(callerUserId))
        {
            var customerIds = await _cases.Query()
                .Where(c => c.AssignedToUserId == callerUserId)
                .Select(c => c.CustomerId)
                .Distinct()
                .ToListAsync();
            q = q.Where(c => customerIds.Contains(c.Id));
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            term = term.Trim().ToLower();
            q = q.Where(c =>
                c.Name.ToLower().Contains(term) ||
                c.Email.ToLower().Contains(term) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }
        return await q.Select(c => new CustomerDto
        {
            Id = c.Id,
            Name = c.Name,
            Email = c.Email,
            Phone = c.Phone,
            Company = c.Company,
            Address = c.Address,
            CaseCount = c.Cases.Count,
            CreatedAtUtc = c.CreatedAtUtc,
        }).OrderBy(c => c.Name).ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<CustomerDto> CreateAsync(CreateCustomerDto dto)
    {
        var customer = new Customer
        {
            Name = dto.Name,
            Email = NormalizeEmail(dto.Email),
            Phone = NormalizePhone(dto.Phone),
            Company = dto.Company,
            Address = dto.Address,
        };
        await _customers.AddAsync(customer);
        await _customers.SaveChangesAsync();
        return ToDto(customer);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(UpdateCustomerDto dto)
    {
        var customer = await _customers.GetByIdAsync(dto.Id)
            ?? throw new KeyNotFoundException($"Customer {dto.Id} not found.");
        customer.Name = dto.Name;
        customer.Email = NormalizeEmail(dto.Email);
        customer.Phone = NormalizePhone(dto.Phone);
        customer.Company = dto.Company;
        customer.Address = dto.Address;
        _customers.Update(customer);
        await _customers.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id)
    {
        var customer = await _customers.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Customer {id} not found.");
        _customers.Remove(customer);
        await _customers.SaveChangesAsync();
    }

    private static CustomerDto ToDto(Customer c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Email = c.Email,
        Phone = c.Phone,
        Company = c.Company,
        Address = c.Address,
        CaseCount = c.Cases?.Count ?? 0,
        CreatedAtUtc = c.CreatedAtUtc,
    };

    private static string NormalizeEmail(string email) => email.Trim().ToLower();

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return phone!.Trim().StartsWith("+") ? "+" + digits : digits;
    }
}
