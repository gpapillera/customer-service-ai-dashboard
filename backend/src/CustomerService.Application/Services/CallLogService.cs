using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="ICallLogService"/>.
/// </summary>
public class CallLogService : ICallLogService
{
    private readonly IRepository<CallLog> _logs;
    private readonly IRepository<Case> _cases;

    /// <summary>Initializes a new <see cref="CallLogService"/>.</summary>
    /// <param name="logs">Call log repository.</param>
    /// <param name="cases">Case repository (existence check).</param>
    public CallLogService(IRepository<CallLog> logs, IRepository<Case> cases)
    {
        _logs = logs;
        _cases = cases;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CallLogDto>> GetByCaseAsync(int caseId, string? callerRole = null, string? callerUserId = null)
    {
        // SERVER-SIDE AGENT SCOPING (Phase 6): an Agent may only read logs for
        // a case assigned to them (unassigned/other-agent cases are forbidden).
        // Admin is unaffected.
        if (string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(callerUserId))
        {
            var c = await _cases.GetByIdAsync(caseId);
            if (c is null || c.AssignedToUserId != callerUserId)
                throw new ForbiddenException("You can only view logs for cases assigned to you.");
        }

        return await _logs.Query()
            .Where(l => l.CaseId == caseId)
            .OrderBy(l => l.CreatedAtUtc)
            .Select(l => new CallLogDto
            {
                Id = l.Id,
                CaseId = l.CaseId,
                Direction = l.Direction,
                Notes = l.Notes,
                DurationSeconds = l.DurationSeconds,
                LoggedByUserId = l.LoggedByUserId,
                CreatedAtUtc = l.CreatedAtUtc,
            })
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<CallLogDto> CreateAsync(CreateCallLogDto dto, string? loggedByUserId, string? callerRole = null, string? callerUserId = null)
    {
        var c = await _cases.GetByIdAsync(dto.CaseId);
        if (c is null)
            throw new KeyNotFoundException($"Case {dto.CaseId} not found.");

        // SERVER-SIDE AGENT SCOPING (Phase 6): an Agent may only add logs to a
        // case assigned to them (unassigned/other-agent cases are forbidden).
        // Admin is unaffected.
        if (string.Equals(callerRole, nameof(UserRole.Agent), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(callerUserId)
            && c.AssignedToUserId != callerUserId)
        {
            throw new ForbiddenException("You can only add logs to cases assigned to you.");
        }

        var log = new CallLog
        {
            CaseId = dto.CaseId,
            Direction = dto.Direction,
            Notes = dto.Notes,
            DurationSeconds = dto.DurationSeconds,
            LoggedByUserId = loggedByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await _logs.AddAsync(log);
        await _logs.SaveChangesAsync();
        return new CallLogDto
        {
            Id = log.Id,
            CaseId = log.CaseId,
            Direction = log.Direction,
            Notes = log.Notes,
            DurationSeconds = log.DurationSeconds,
            LoggedByUserId = log.LoggedByUserId,
            CreatedAtUtc = log.CreatedAtUtc,
        };
    }
}
