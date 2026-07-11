using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for call-log operations.</summary>
public interface ICallLogService
{
    /// <summary>Returns all call logs for a case.</summary>
    /// <param name="caseId">Parent case id.</param>
    /// <returns>Call logs ordered by creation time.</returns>
    Task<IReadOnlyList<CallLogDto>> GetByCaseAsync(int caseId);

    /// <summary>Adds a call log to a case.</summary>
    /// <param name="dto">Create payload.</param>
    /// <param name="loggedByUserId">Agent id performing the log.</param>
    /// <returns>The created <see cref="CallLogDto"/>.</returns>
    Task<CallLogDto> CreateAsync(CreateCallLogDto dto, string? loggedByUserId);
}
