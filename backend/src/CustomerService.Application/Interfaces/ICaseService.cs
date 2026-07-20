using CustomerService.Application.Dtos;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for case operations.</summary>
public interface ICaseService
{
    /// <summary>Returns cases, optionally filtered.</summary>
    /// <param name="status">Optional status filter.</param>
    /// <param name="priority">Optional priority filter.</param>
    /// <param name="categoryId">Optional category filter.</param>
    /// <param name="from">Optional created-from date (UTC).</param>
    /// <param name="to">Optional created-to date (UTC).</param>
    /// <param name="overdue">When true, only open cases with a past follow-up deadline and no follow-up since.</param>
    /// <param name="assignedToUserId">When set, only cases assigned to this user id (resolved from the JWT by the controller).</param>
    /// <returns>Matching cases.</returns>
    Task<IReadOnlyList<CaseDto>> GetAllAsync(CaseStatus? status, Priority? priority, int? categoryId, DateTime? from, DateTime? to, bool overdue = false, string? assignedToUserId = null);

    /// <summary>Returns a single case by id.</summary>
    /// <param name="id">Case id.</param>
    /// <returns>The <see cref="CaseDto"/> or null.</returns>
    Task<CaseDto?> GetByIdAsync(int id);

    /// <summary>Creates a case, auto-suggesting priority via ML when not supplied.</summary>
    /// <param name="dto">Create payload.</param>
    /// <returns>The created <see cref="CaseDto"/>.</returns>
    Task<CaseDto> CreateAsync(CreateCaseDto dto);

    /// <summary>Updates a case (priority override allowed).</summary>
    /// <param name="id">Case id.</param>
    /// <param name="dto">Update payload.</param>
    Task UpdateAsync(int id, UpdateCaseDto dto);

    /// <summary>Deletes a case.</summary>
    /// <param name="id">Case id.</param>
    Task DeleteAsync(int id);
}
