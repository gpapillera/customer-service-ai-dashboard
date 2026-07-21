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
        bool overdue = false, string? assignedToUserId = null, string? callerRole = null, string? callerUserId = null) =>
        Task.FromResult<IReadOnlyList<CaseDto>>(new List<CaseDto>());

    public Task<CaseDto?> GetByIdAsync(int id, string? callerRole = null, string? callerUserId = null) => Task.FromResult<CaseDto?>(null);

    public Task<CaseDto> CreateAsync(CreateCaseDto dto) =>
        Task.FromResult(new CaseDto { Id = 0, Subject = dto.Subject, CustomerId = dto.CustomerId });

    public Task UpdateAsync(int id, UpdateCaseDto dto, string? callerRole = null, string? callerUserId = null) => Task.CompletedTask;

    public Task DeleteAsync(int id) => Task.CompletedTask;

    public Task<IReadOnlyList<ConversationSummaryDto>> GetMyConversationsAsync(string agentUserId) =>
        Task.FromResult<IReadOnlyList<ConversationSummaryDto>>(new List<ConversationSummaryDto>());

    public Task MarkConversationReadAsync(int caseId, string agentUserId) => Task.CompletedTask;

    public Task<IReadOnlyList<ConversationSummaryDto>> GetAllConversationsAsync() =>
        Task.FromResult<IReadOnlyList<ConversationSummaryDto>>(new List<ConversationSummaryDto>());
}
