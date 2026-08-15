using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;

namespace CustomerService.Tests.Fakes;

/// <summary>
/// No-op <see cref="IViewEventService"/> for unit tests that don't exercise
/// the view-audit path. Records nothing and returns empty timelines.
/// </summary>
public class FakeViewEventService : IViewEventService
{
    public Task<ViewEventDto?> RecordViewAsync(string targetType, int targetId, string? viewerUserId, string viewerName, string? viewerRole, DateTime? now = null)
        => Task.FromResult<ViewEventDto?>(null);

    public Task<IReadOnlyList<ViewEventDto>> GetForTargetAsync(string targetType, int targetId)
        => Task.FromResult<IReadOnlyList<ViewEventDto>>(Array.Empty<ViewEventDto>());

    public Task<IReadOnlyList<ViewEventDto>> GetForCustomerAsync(int customerId, IReadOnlyList<int> caseIds)
        => Task.FromResult<IReadOnlyList<ViewEventDto>>(Array.Empty<ViewEventDto>());
}
