using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
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
    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync()
    {
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
            })
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<CustomerDto?> GetByIdAsync(int id)
    {
        var c = await _customers.GetByIdAsync(id);
        return c is null ? null : ToDto(c);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomerDto>> SearchAsync(string? term)
    {
        var q = _customers.Query();
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
    };

    private static string NormalizeEmail(string email) => email.Trim().ToLower();

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return phone!.Trim().StartsWith("+") ? "+" + digits : digits;
    }
}
