using System.Security.Cryptography;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Default <see cref="IRefreshTokenService"/> backed by the application
/// database. Tokens are 64-byte cryptographically-random opaque strings.
/// Rotation revokes the prior token and links it to the replacement, so a
/// replayed (already-rotated) token is detectable as a chain break.
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
    private readonly IRepository<RefreshToken> _tokens;

    public RefreshTokenService(IRepository<RefreshToken> tokens)
    {
        _tokens = tokens;
    }

    public async Task<string> CreateAsync(string subjectId, string subjectType, string role, int daysValid)
    {
        var token = GenerateToken();
        await _tokens.AddAsync(new RefreshToken
        {
            Token = token,
            SubjectId = subjectId,
            SubjectType = subjectType,
            Role = role,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddDays(daysValid),
        });
        await _tokens.SaveChangesAsync();
        return token;
    }

    public async Task<(bool Ok, RefreshToken? Token)> ValidateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, null);
        }

        // Tracked load so a later Revoke/Rotate mutates the same instance.
        var row = await _tokens.QueryTracked()
            .FirstOrDefaultAsync(t => t.Token == token);
        if (row is null || row.RevokedUtc is not null || row.ExpiresUtc < DateTime.UtcNow)
        {
            return (false, row);
        }

        return (true, row);
    }

    public async Task<string> RotateAsync(string oldToken)
    {
        var (ok, row) = await ValidateAsync(oldToken);
        if (!ok || row is null)
        {
            throw new InvalidOperationException("Cannot rotate an invalid refresh token.");
        }

        var newToken = GenerateToken();
        row.RevokedUtc = DateTime.UtcNow;
        row.ReplacedByToken = newToken;
        _tokens.Update(row);

        await _tokens.AddAsync(new RefreshToken
        {
            Token = newToken,
            SubjectId = row.SubjectId,
            SubjectType = row.SubjectType,
            Role = row.Role,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddDays((row.ExpiresUtc - row.CreatedUtc).Days),
        });
        await _tokens.SaveChangesAsync();
        return newToken;
    }

    public async Task RevokeAsync(string token)
    {
        var row = await _tokens.QueryTracked()
            .FirstOrDefaultAsync(t => t.Token == token);
        if (row is null)
        {
            return;
        }

        row.RevokedUtc = DateTime.UtcNow;
        _tokens.Update(row);
        await _tokens.SaveChangesAsync();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
