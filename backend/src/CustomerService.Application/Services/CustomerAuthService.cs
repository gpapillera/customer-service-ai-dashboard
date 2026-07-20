using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="ICustomerAuthService"/>: customer portal invites,
/// invite acceptance (BCrypt password set), and customer login (distinct JWT
/// with role = "Customer"). Kept separate from staff <see cref="AuthService"/>.
/// See docs/DIY.md §8 (customer authentication).
/// </summary>
public class CustomerAuthService : ICustomerAuthService
{
    private const int InviteValidHours = 48;

    private readonly IRepository<Customer> _customers;
    private readonly IRepository<CustomerAccount> _accounts;
    private readonly INotificationSender _sender;
    private readonly IConfiguration _config;

    /// <summary>Initializes a new <see cref="CustomerAuthService"/>.</summary>
    public CustomerAuthService(
        IRepository<Customer> customers,
        IRepository<CustomerAccount> accounts,
        INotificationSender sender,
        IConfiguration config)
    {
        _customers = customers;
        _accounts = accounts;
        _sender = sender;
        _config = config;
    }

    /// <inheritdoc/>
    public async Task<string> SendInviteAsync(int customerId)
    {
        var customer = await _customers.GetByIdAsync(customerId)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        if (string.IsNullOrWhiteSpace(customer.Email))
        {
            throw new InvalidOperationException("Customer has no email address on file.");
        }

        var account = await _accounts.Query()
            .FirstOrDefaultAsync(a => a.CustomerId == customerId);
        if (account is null)
        {
            account = new CustomerAccount { CustomerId = customerId };
            await _accounts.AddAsync(account);
        }
        else
        {
            _accounts.Update(account);
        }

        // Overwrite any previous unused invite for this customer.
        account.InviteToken = Guid.NewGuid().ToString("N");
        account.InviteTokenExpiresAt = DateTime.UtcNow.AddHours(InviteValidHours);
        account.InviteTokenUsed = false;
        await _accounts.SaveChangesAsync();

        var frontendBase = _config["FrontendBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";
        var link = $"{frontendBase}/customer/accept-invite?token={account.InviteToken}";
        var message = $"You've been invited to set up your secure customer portal account. "
            + $"Click the link below to choose a password and activate your account:\n\n{link}\n\n"
            + $"This invitation expires in {InviteValidHours} hours.";

        var notification = new Notification
        {
            Title = "Customer portal invitation",
            Message = message,
            Channel = NotificationChannel.Email,
            Type = NotificationType.CustomerInvite,
            Status = NotificationStatus.Unread,
            CreatedAtUtc = DateTime.UtcNow,
            Recipient = customer.Email,
        };
        await _sender.SendAsync(notification);

        return account.InviteToken;
    }

    /// <inheritdoc/>
    public async Task<ValidateInviteResponse> ValidateInviteAsync(string token)
    {
        var account = await FindByTokenAsync(token);
        if (account is null || account.InviteTokenUsed || account.InviteTokenExpiresAt < DateTime.UtcNow)
        {
            return new ValidateInviteResponse { Valid = false };
        }

        var customer = await _customers.GetByIdAsync(account.CustomerId);
        return new ValidateInviteResponse
        {
            Valid = true,
            CustomerName = customer?.Name,
            CustomerEmailMasked = MaskEmail(customer?.Email),
        };
    }

    /// <inheritdoc/>
    public async Task AcceptInviteAsync(AcceptInviteRequest request)
    {
        var account = await FindByTokenAsync(request.Token)
            ?? throw new InvalidOperationException("Invalid invite token.");

        if (account.InviteTokenUsed)
        {
            throw new InvalidOperationException("This invite has already been used.");
        }

        if (account.InviteTokenExpiresAt < DateTime.UtcNow)
        {
            throw new InvalidOperationException("This invite has expired.");
        }

        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        account.IsActive = true;
        account.InviteTokenUsed = true;
        _accounts.Update(account);
        await _accounts.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<CustomerLoginResponse?> LoginAsync(CustomerLoginRequest request)
    {
        var customer = await _customers.Query()
            .FirstOrDefaultAsync(c => c.Email == request.Email.Trim().ToLower());
        if (customer is null)
        {
            return null;
        }

        var account = await _accounts.Query()
            .FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
        if (account is null || !account.IsActive || string.IsNullOrWhiteSpace(account.PasswordHash))
        {
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
        {
            return null;
        }

        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "dev-insecure-key-change-me-1234567890");
        var expires = DateTime.UtcNow.AddHours(8);
        var securityToken = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString()),
                new Claim(ClaimTypes.Name, customer.Email),
                new Claim(ClaimTypes.Role, "Customer"),
                new Claim("CustomerId", customer.Id.ToString()),
            },
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        var tokenString = new JwtSecurityTokenHandler().WriteToken(securityToken);

        return new CustomerLoginResponse
        {
            Token = tokenString,
            ExpiresUtc = expires,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            Role = "Customer",
        };
    }

    private async Task<CustomerAccount?> FindByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }
        return await _accounts.Query().FirstOrDefaultAsync(a => a.InviteToken == token);
    }

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return email;
        }
        var parts = email.Split('@', 2);
        var local = parts[0];
        var maskedLocal = local.Length <= 1 ? local : local[0] + new string('*', Math.Min(local.Length - 1, 3));
        return $"{maskedLocal}@{parts[1]}";
    }
}
