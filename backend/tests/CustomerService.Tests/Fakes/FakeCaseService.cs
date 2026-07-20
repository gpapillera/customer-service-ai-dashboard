using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;

namespace CustomerService.Tests.Fakes;

/// <summary>
/// Minimal <see cref="ICaseService"/> fake for unit tests that only need the
/// controller to construct (e.g. the auth-boundary tests, which exercise the
/// portal's GET/comment paths, not case creation).
/// </summary>
public class FakeCaseService : ICaseService
{
    public Task<IReadOnlyList<CaseDto>> GetAllAsync(
        CaseStatus? status, Priority? priority, int? categoryId, DateTime? from, DateTime? to,
        bool overdue = false, string? assignedToUserId = null) =>
        Task.FromResult<IReadOnlyList<CaseDto>>(new List<CaseDto>());

    public Task<CaseDto?> GetByIdAsync(int id) => Task.FromResult<CaseDto?>(null);

    public Task<CaseDto> CreateAsync(CreateCaseDto dto) =>
        Task.FromResult(new CaseDto { Id = 0, Subject = dto.Subject, CustomerId = dto.CustomerId });

    public Task UpdateAsync(int id, UpdateCaseDto dto) => Task.CompletedTask;

    public Task DeleteAsync(int id) => Task.CompletedTask;
}
