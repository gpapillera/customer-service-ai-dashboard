using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerService.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="IAuthService"/>: validates credentials (BCrypt) and
/// mints a JWT with the user's role claim. Also handles staff profile
/// management and password-reset flows.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IRepository<User> _users;
    private readonly IConfiguration _config;
    private readonly INotificationSender _sender;
    private readonly ILogger<AuthService> _logger;

    /// <summary>Initializes a new <see cref="AuthService"/>.</summary>
    public AuthService(
        IRepository<User> users,
        IConfiguration config,
        INotificationSender sender,
        ILogger<AuthService> logger)
    {
        _users = users;
        _config = config;
        _sender = sender;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _users.Query()
            .FirstOrDefaultAsync(u => u.UserName == request.UserName);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
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
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            },
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        var tokenString = new JwtSecurityTokenHandler().WriteToken(securityToken);

        return new LoginResponse
        {
            Id = user.Id,
            Token = tokenString,
            ExpiresUtc = expires,
            UserName = user.UserName,
            FullName = user.FullName,
            Role = user.Role.ToString(),
        };
    }

    /// <inheritdoc/>
    public async Task<StaffProfileDto> GetProfileAsync(string userId)
    {
        var user = await _users.Query().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        return new StaffProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            UserName = user.UserName,
            Role = user.Role.ToString(),
        };
    }

    /// <inheritdoc/>
    public async Task UpdateProfileAsync(string userId, UpdateStaffProfileDto dto)
    {
        var user = await _users.Query().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        user.FullName = dto.FullName;
        _users.Update(user);
        await _users.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task RequestPasswordResetAsync(string userId)
    {
        var user = await _users.Query().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        // Generate a fresh reset token (distinct from any customer invite token).
        user.ResetToken = Guid.NewGuid().ToString("N");
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddHours(48);
        user.ResetTokenUsed = false;
        _users.Update(user);
        await _users.SaveChangesAsync();

        // Build the reset link and email a notification using the existing
        // infrastructure (same pattern as CustomerAuthService).
        var frontendBaseUrl = _config["FrontendBaseUrl"] ?? "http://localhost:4200";
        var resetLink = $"{frontendBaseUrl}/reset-password?token={user.ResetToken}";

        var notification = new Notification
        {
            Title = "Password Reset Request",
            Message = $"Click the link below to set a new password. This link expires in 48 hours.\n\n{resetLink}",
            Channel = NotificationChannel.Email,
            Type = NotificationType.StaffPasswordReset,
            Recipient = user.Email,
        };

        try
        {
            await _sender.SendAsync(notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send staff password-reset email to {Email}", user.Email);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _users.Query()
            .FirstOrDefaultAsync(u => u.ResetToken == request.Token);
        if (user is null)
        {
            return false;
        }

        // Validate: not expired, not already used.
        if (user.ResetTokenUsed)
        {
            _logger.LogWarning("Password reset attempted with already-used token for user {UserId}", user.Id);
            return false;
        }
        if (user.ResetTokenExpiresAt is null || user.ResetTokenExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Password reset attempted with expired token for user {UserId}", user.Id);
            return false;
        }

        // Set the new password and invalidate the token.
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        user.ResetToken = null;
        user.ResetTokenExpiresAt = null;
        user.ResetTokenUsed = true;
        _users.Update(user);
        await _users.SaveChangesAsync();
        return true;
    }
}
