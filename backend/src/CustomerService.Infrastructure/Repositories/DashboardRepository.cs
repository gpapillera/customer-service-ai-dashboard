using CustomerService.Domain;
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
    public async Task<DashboardSummary> GetSummaryAsync(string? agentId = null)
    {
        var cases = _context.Cases;

        // For an agent, the status/priority breakdowns are scoped to their own
        // assigned cases (the "My *" view). For admin (no agentId) they stay
        // company-wide.
        var scoped = agentId is not null
            ? cases.Where(c => c.AssignedToUserId == agentId)
            : cases;

        var total = await cases.CountAsync();
        var closed = await cases.CountAsync(c => c.Status == CaseStatus.Closed);
        var resolved = await cases.CountAsync(c => c.Status == CaseStatus.Resolved);
        var high = await cases.CountAsync(c => c.Priority == Priority.High);
        var aiPredicted = await cases.CountAsync(c => c.PriorityAutoSuggested);
        var open = total - closed;

        // Build the status/priority dictionaries defensively: a single
        // malformed row (e.g. a status stored as text instead of the enum's
        // integer value) must not crash the whole dashboard. Sum on collision
        // so the aggregate stays correct instead of throwing on a duplicate key.
        var byStatus = new Dictionary<string, int>();
        foreach (var g in await scoped.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
        {
            var key = g.Key.ToString();
            byStatus.TryGetValue(key, out var existing);
            byStatus[key] = existing + g.Count;
        }

        var byPriority = new Dictionary<string, int>();
        foreach (var g in await scoped.GroupBy(c => c.Priority).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
        {
            var key = g.Key.ToString();
            byPriority.TryGetValue(key, out var existing);
            byPriority[key] = existing + g.Count;
        }

        var totalCustomers = await _context.Customers.CountAsync();

        var overdue = await GetOverdueFollowUpsAsync(agentId);

        // Agent-scoped ("My *") totals — only when an agent id is supplied.
        var myCases = 0;
        var myOpen = 0;
        var myHigh = 0;
        var myAi = 0;
        var myResolved = 0;
        var myOverdue = 0;
        if (agentId is not null)
        {
            var assigned = cases.Where(c => c.AssignedToUserId == agentId);
            myCases = await assigned.CountAsync();
            myOpen = await assigned.CountAsync(c =>
                c.Status != CaseStatus.Resolved && c.Status != CaseStatus.Closed);
            myHigh = await assigned.CountAsync(c => c.Priority == Priority.High);
            myAi = await assigned.CountAsync(c => c.PriorityAutoSuggested);
            myResolved = await assigned.CountAsync(c => c.Status == CaseStatus.Resolved);
            myOverdue = overdue.Count;
        }

        return new DashboardSummary
        {
            TotalCases = total,
            OpenCases = open,
            ClosedCases = closed,
            ResolvedCases = resolved,
            AiPredictedCases = aiPredicted,
            HighPriorityCases = high,
            TotalCustomers = totalCustomers,
            MyCases = myCases,
            MyOpenCases = myOpen,
            MyHighPriorityCases = myHigh,
            MyAiPredictedCases = myAi,
            MyResolvedCases = myResolved,
            MyOverdueFollowUps = myOverdue,
            ByStatus = byStatus,
            ByPriority = byPriority,
            OverdueFollowUps = overdue.Count,
            OverdueFollowUpDetails = overdue,
        };
    }

    /// <inheritdoc/>
    public async Task<List<OverdueFollowUpSummary>> GetOverdueFollowUpsAsync(string? agentId = null)
    {
        var now = DateTime.UtcNow;
        // Open cases that need a follow-up (scheduled deadline missed OR stale
        // with no follow-up). Uses the shared OverduePolicy so this matches the
        // cases filter and the notification generator exactly.
        var query = _context.Cases
            .Include(c => c.Customer)
            .Include(c => c.AssignedToUser)
            .Include(c => c.CallLogs)
            .Where(c => OverduePolicy.OpenStatuses.Contains(c.Status));
        // Agent view: only their own assigned cases.
        if (agentId is not null)
        {
            query = query.Where(c => c.AssignedToUserId == agentId);
        }
        var candidates = await query.ToListAsync();

        var result = new List<OverdueFollowUpSummary>();
        foreach (var c in candidates)
        {
            if (!OverduePolicy.NeedsFollowUp(c, now))
            {
                continue;
            }

            var due = c.FollowUpDueUtc ?? now.AddDays(-OverduePolicy.StaleDays);
            result.Add(new OverdueFollowUpSummary
            {
                CaseId = c.Id,
                Subject = c.Subject,
                CustomerName = c.Customer?.Name ?? string.Empty,
                AssignedToUserName = c.AssignedToUser?.FullName ?? string.Empty,
                Priority = c.Priority,
                FollowUpDueUtc = due,
                DaysOverdue = OverduePolicy.DaysOverdue(c, now),
            });
        }

        // Most overdue first.
        result.Sort((a, b) => b.DaysOverdue.CompareTo(a.DaysOverdue));
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DateCount>> GetCasesCreatedTrendAsync(int days, string? agentId = null)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var query = _context.Cases.Where(c => c.CreatedAtUtc >= since);
        if (agentId is not null)
        {
            query = query.Where(c => c.AssignedToUserId == agentId);
        }
        var data = await query
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
    public async Task<IReadOnlyList<CategoryCount>> GetCasesByCategoryAsync(string? agentId = null)
    {
        var query = _context.Cases.AsQueryable();
        if (agentId is not null)
        {
            query = query.Where(c => c.AssignedToUserId == agentId);
        }
        return await query
            .GroupBy(c => c.Category!.Name)
            .Select(g => new CategoryCount { Category = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Case>> GetRecentCasesAsync(int limit, string? agentId = null)
    {
        var query = _context.Cases
            .Include(c => c.Customer)
            .Include(c => c.Category)
            .AsQueryable();
        if (agentId is not null)
        {
            query = query.Where(c => c.AssignedToUserId == agentId);
        }
        return await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(limit)
            .ToListAsync();
    }
}
