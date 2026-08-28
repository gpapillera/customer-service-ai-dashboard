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
    private readonly ILiveUpdateHub _events;

    /// <summary>Initializes a new <see cref="CallLogService"/>.</summary>
    /// <param name="logs">Call log repository.</param>
    /// <param name="cases">Case repository (existence check).</param>
    /// <param name="events">Unified realtime hub (live push on create).</param>
    public CallLogService(IRepository<CallLog> logs, IRepository<Case> cases, ILiveUpdateHub events)
    {
        _logs = logs;
        _cases = cases;
        _events = events;
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
            // A soft-deleted case is unreachable through the normal query path,
            // so it resolves to null here — treat it exactly like a missing case
            // rather than letting the Agent-scope check below mask the cause.
            if (c is null)
                throw new KeyNotFoundException($"Case {caseId} not found.");
            if (c.AssignedToUserId != callerUserId)
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
        // A soft-deleted case can't receive new call logs. The repository query
        // already hides binned rows, so this is the same "missing" outcome from
        // the caller's perspective — return a clean 404 rather than letting it
        // look like an unhandled server error in the logs.
        if (c.IsDeleted)
            throw new KeyNotFoundException($"Case {dto.CaseId} has been deleted and can't receive call logs.");

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

        // Push a "case-update" so the customer card footer ("Updated call log")
        // and any open case/conversation view reflect the new log instantly
        // (no manual refresh). The customer-list effect reloads on ANY event.
        try
        {
            await _events.PublishAsync(new LiveUpdateEvent(
                "case-update", CaseId: dto.CaseId, CustomerId: c.CustomerId,
                ActorUserId: loggedByUserId, ActorRole: callerRole));
        }
        catch
        {
            // Swallow — realtime is best-effort.
        }

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
