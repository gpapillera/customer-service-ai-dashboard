using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerService.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="IAuthService"/>: validates credentials (BCrypt) and
/// mints a JWT with the user's role claim.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IRepository<User> _users;
    private readonly IConfiguration _config;

    /// <summary>Initializes a new <see cref="AuthService"/>.</summary>
    /// <param name="users">User repository.</param>
    /// <param name="config">App configuration (for JWT settings).</param>
    public AuthService(IRepository<User> users, IConfiguration config)
    {
        _users = users;
        _config = config;
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
}
