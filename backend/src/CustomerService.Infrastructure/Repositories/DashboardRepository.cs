using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDashboardRepository"/>.
/// </summary>
public class DashboardRepository : IDashboardRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new <see cref="DashboardRepository"/>.</summary>
    /// <param name="context">The database context.</param>
    public DashboardRepository(AppDbContext context) => _context = context;

    /// <inheritdoc/>
    public async Task<DashboardSummary> GetSummaryAsync()
    {
        var cases = _context.Cases;
        var total = await cases.CountAsync();
        var closed = await cases.CountAsync(c => c.Status == CaseStatus.Closed);
        var resolved = await cases.CountAsync(c => c.Status == CaseStatus.Resolved);
        var high = await cases.CountAsync(c => c.Priority == Priority.High);
        var aiPredicted = await cases.CountAsync(c => c.PriorityAutoSuggested);
        var open = total - closed;

        var byStatus = await cases.GroupBy(c => c.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key.ToString(), g => g.Count);
        var byPriority = await cases.GroupBy(c => c.Priority)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key.ToString(), g => g.Count);

        var totalCustomers = await _context.Customers.CountAsync();

        var overdue = await GetOverdueFollowUpsAsync();

        return new DashboardSummary
        {
            TotalCases = total,
            OpenCases = open,
            ClosedCases = closed,
            ResolvedCases = resolved,
            AiPredictedCases = aiPredicted,
            HighPriorityCases = high,
            TotalCustomers = totalCustomers,
            ByStatus = byStatus,
            ByPriority = byPriority,
            OverdueFollowUps = overdue.Count,
            OverdueFollowUpDetails = overdue,
        };
    }

    /// <inheritdoc/>
    public async Task<List<OverdueFollowUpSummary>> GetOverdueFollowUpsAsync()
    {
        var now = DateTime.UtcNow;
        // Open cases with a follow-up deadline in the past.
        var openStatuses = new[] { CaseStatus.New, CaseStatus.InProgress, CaseStatus.Escalated };
        var candidates = await _context.Cases
            .Include(c => c.Customer)
            .Include(c => c.AssignedToUser)
            .Include(c => c.CallLogs)
            .Where(c => c.FollowUpDueUtc.HasValue && c.FollowUpDueUtc.Value < now)
            .Where(c => openStatuses.Contains(c.Status))
            .ToListAsync();

        var result = new List<OverdueFollowUpSummary>();
        foreach (var c in candidates)
        {
            var due = c.FollowUpDueUtc!.Value;
            // Not overdue if a follow-up (call log) happened on/after the deadline.
            var followedUpSince = c.CallLogs.Any(cl => cl.CreatedAtUtc >= due);
            if (followedUpSince)
            {
                continue;
            }

            var daysOverdue = (int)Math.Ceiling((now - due).TotalDays);
            result.Add(new OverdueFollowUpSummary
            {
                CaseId = c.Id,
                Subject = c.Subject,
                CustomerName = c.Customer?.Name ?? string.Empty,
                AssignedToUserName = c.AssignedToUser?.FullName ?? string.Empty,
                Priority = c.Priority,
                FollowUpDueUtc = due,
                DaysOverdue = daysOverdue < 1 ? 1 : daysOverdue,
            });
        }

        // Most overdue first.
        result.Sort((a, b) => b.DaysOverdue.CompareTo(a.DaysOverdue));
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DateCount>> GetCasesCreatedTrendAsync(int days)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var data = await _context.Cases
            .Where(c => c.CreatedAtUtc >= since)
            .GroupBy(c => c.CreatedAtUtc.Date)
            .Select(g => new DateCount { Date = g.Key, Count = g.Count() })
            .OrderBy(g => g.Date)
            .ToListAsync();

        // Fill gaps so the chart has a continuous x-axis.
        var result = new List<DateCount>();
        for (var d = since; d <= DateTime.UtcNow.Date; d = d.AddDays(1))
        {
            var found = data.FirstOrDefault(x => x.Date == d);
            result.Add(found ?? new DateCount { Date = d, Count = 0 });
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CategoryCount>> GetCasesByCategoryAsync()
    {
        return await _context.Cases
            .GroupBy(c => c.Category!.Name)
            .Select(g => new CategoryCount { Category = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Case>> GetRecentCasesAsync(int limit)
    {
        return await _context.Cases
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(limit)
            .ToListAsync();
    }
}
