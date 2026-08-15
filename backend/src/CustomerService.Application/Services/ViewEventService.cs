using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Application.Services;

/// <summary>
/// Default <see cref="IViewEventService"/>: persists view events to the
/// <c>ViewEvents</c> table and coalesces repeats per viewer by a cooldown
/// window so re-opening the same page within that window adds at most one row.
/// </summary>
public class ViewEventService : IViewEventService
{
    // ponytail: a simple fixed cooldown. Good enough for an audit timeline; if
    // you ever need per-viewer-type windows, promote this to a config value.
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private readonly IRepository<ViewEvent> _viewEvents;

    public ViewEventService(IRepository<ViewEvent> viewEvents)
    {
        _viewEvents = viewEvents;
    }

    public async Task<ViewEventDto?> RecordViewAsync(string targetType, int targetId, string? viewerUserId, string viewerName, string? viewerRole, DateTime? now = null)
    {
        var stamp = now ?? DateTime.UtcNow;
        var cutoff = stamp - Cooldown;

        var recent = await _viewEvents.Query()
            .Where(v => v.TargetType == targetType && v.TargetId == targetId && v.ViewerUserId == viewerUserId && v.AtUtc > cutoff)
            .OrderByDescending(v => v.AtUtc)
            .FirstOrDefaultAsync();

        if (recent is not null)
        {
            // Coalesced: a recent view by this viewer already covers this open.
            return null;
        }

        var row = new ViewEvent
        {
            TargetType = targetType,
            TargetId = targetId,
            ViewerUserId = viewerUserId,
            ViewerName = viewerName,
            ViewerRole = viewerRole,
            AtUtc = stamp,
        };
        await _viewEvents.AddAsync(row);
        await _viewEvents.SaveChangesAsync();

        return ToDto(row);
    }

    public async Task<IReadOnlyList<ViewEventDto>> GetForTargetAsync(string targetType, int targetId)
    {
        var rows = await _viewEvents.Query()
            .Where(v => v.TargetType == targetType && v.TargetId == targetId)
            .OrderByDescending(v => v.AtUtc)
            .ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ViewEventDto>> GetForCustomerAsync(int customerId, IReadOnlyList<int> caseIds)
    {
        if (caseIds.Count == 0)
        {
            return await GetForTargetAsync("Customer", customerId);
        }

        var rows = await _viewEvents.Query()
            .Where(v => (v.TargetType == "Customer" && v.TargetId == customerId) ||
                        (v.TargetType == "Case" && caseIds.Contains(v.TargetId)))
            .OrderByDescending(v => v.AtUtc)
            .ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    private static ViewEventDto ToDto(ViewEvent v) => new()
    {
        Id = v.Id,
        TargetType = v.TargetType,
        TargetId = v.TargetId,
        ViewerUserId = v.ViewerUserId,
        ViewerName = v.ViewerName,
        ViewerRole = v.ViewerRole,
        AtUtc = v.AtUtc,
    };
}
