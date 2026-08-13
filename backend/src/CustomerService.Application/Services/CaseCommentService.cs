using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="ICaseCommentService"/>: the shared case-comment thread
/// used by both the customer portal and staff. The "exactly one author" rule is
/// enforced here at creation time — a staff comment sets <c>AuthorUserId</c>
/// only, a customer comment sets <c>AuthorCustomerId</c> only. Never both,
/// never neither.
/// </summary>
public class CaseCommentService : ICaseCommentService
{
    private readonly IRepository<Case> _cases;
    private readonly IRepository<CaseComment> _comments;
    private readonly IRepository<User> _users;
    private readonly IRepository<Customer> _customers;

    /// <summary>Initializes a new <see cref="CaseCommentService"/>.</summary>
    public CaseCommentService(
        IRepository<Case> cases,
        IRepository<CaseComment> comments,
        IRepository<User> users,
        IRepository<Customer> customers)
    {
        _cases = cases;
        _comments = comments;
        _users = users;
        _customers = customers;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaseCommentDto>?> GetCommentsAsync(int caseId)
    {
        if (await _cases.GetByIdAsync(caseId) is null)
        {
            return null;
        }

        var comments = await _comments.Query()
            .Include(c => c.AuthorUser)
            .Include(c => c.AuthorCustomer)
            .Where(c => c.CaseId == caseId)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync();

        return comments.Select(ToDto).ToList();
    }

    /// <inheritdoc/>
    public async Task<CaseCommentDto> AddStaffCommentAsync(int caseId, string authorUserId, string body,
        string? callerRole = null, string? callerUserId = null)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Comment body must not be empty.", nameof(body));
        }
        var c = await _cases.GetByIdAsync(caseId);
        if (c is null)
        {
            throw new KeyNotFoundException($"Case {caseId} not found.");
        }

        // SERVER-SIDE AGENT SCOPING (Phase 6, mirrors CallLogService): an Agent
        // may only post to a case assigned to them. Unassigned/other-agent cases
        // are forbidden even though the case exists — the read-only banner on the
        // case detail page means exactly this, and a frontend guard alone is not
        // enough (a crafted request could bypass it). Admin is unaffected.
        if (string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(callerUserId)
            && c.AssignedToUserId != callerUserId)
        {
            throw new ForbiddenException("You can only reply to cases assigned to you.");
        }

        if (await _users.GetByIdAsync(authorUserId) is null)
        {
            throw new KeyNotFoundException($"User {authorUserId} not found.");
        }

        var comment = new CaseComment
        {
            CaseId = caseId,
            AuthorUserId = authorUserId,
            AuthorCustomerId = null,
            Body = body.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        await _comments.AddAsync(comment);
        await _comments.SaveChangesAsync();

        // Re-load author name for the DTO.
        comment.AuthorUser = await _users.GetByIdAsync(authorUserId);
        return ToDto(comment);
    }

    /// <inheritdoc/>
    public async Task<CaseCommentDto> AddCustomerCommentAsync(int caseId, int authorCustomerId, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Comment body must not be empty.", nameof(body));
        }
        if (await _cases.GetByIdAsync(caseId) is null)
        {
            throw new KeyNotFoundException($"Case {caseId} not found.");
        }
        if (await _customers.GetByIdAsync(authorCustomerId) is null)
        {
            throw new KeyNotFoundException($"Customer {authorCustomerId} not found.");
        }

        var comment = new CaseComment
        {
            CaseId = caseId,
            AuthorUserId = null,
            AuthorCustomerId = authorCustomerId,
            Body = body.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        await _comments.AddAsync(comment);
        await _comments.SaveChangesAsync();

        comment.AuthorCustomer = await _customers.GetByIdAsync(authorCustomerId);
        return ToDto(comment);
    }

    private static CaseCommentDto ToDto(CaseComment c) => new()
    {
        Id = c.Id,
        AuthorDisplayName = c.AuthorUser?.FullName
            ?? c.AuthorCustomer?.Name
            ?? (c.AuthorUserId != null ? c.AuthorUserId : $"Customer #{c.AuthorCustomerId}"),
        IsStaff = c.AuthorUserId != null,
        Body = c.Body,
        CreatedAtUtc = c.CreatedAtUtc,
    };
}
