using System.ComponentModel.DataAnnotations;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>Data transfer object for creating a customer.</summary>
public class CreateCustomerDto
{
    /// <summary>Customer full name.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Email address.</summary>
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

/// <summary>Data transfer object for updating a customer.</summary>
public class UpdateCustomerDto : CreateCustomerDto
{
    /// <summary>Customer primary key.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A valid id is required.")]
    public int Id { get; set; }
}

/// <summary>Read model for a customer (includes case count).</summary>
public class CustomerDto
{
    /// <summary>Customer primary key.</summary>
    public int Id { get; set; }

    /// <summary>Customer full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Company name.</summary>
    public string? Company { get; set; }

    /// <summary>Address.</summary>
    public string? Address { get; set; }

    /// <summary>Number of cases raised by this customer.</summary>
    public int CaseCount { get; set; }

    /// <summary>UTC timestamp when the customer record was created.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
