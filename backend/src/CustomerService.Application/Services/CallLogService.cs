using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
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
    public async Task<IReadOnlyList<CallLogDto>> GetByCaseAsync(int caseId)
    {
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
    public async Task<CallLogDto> CreateAsync(CreateCallLogDto dto, string? loggedByUserId)
    {
        if (await _cases.GetByIdAsync(dto.CaseId) is null)
            throw new KeyNotFoundException($"Case {dto.CaseId} not found.");

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
