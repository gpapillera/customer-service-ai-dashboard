using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Domain.Interfaces;

namespace CustomerService.Application.Services;

/// <summary>
/// Implements <see cref="IDashboardService"/> by composing the dashboard
/// repository aggregates into the API DTO.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _repo;

    /// <summary>Initializes a new <see cref="DashboardService"/>.</summary>
    /// <param name="repo">Dashboard repository.</param>
    public DashboardService(IDashboardRepository repo) => _repo = repo;

    /// <inheritdoc/>
    public async Task<DashboardDto> GetDashboardAsync()
    {
        var summary = await _repo.GetSummaryAsync();
        var trend = await _repo.GetCasesCreatedTrendAsync(30);
        var byCategory = await _repo.GetCasesByCategoryAsync();
        var recent = await _repo.GetRecentCasesAsync(5);

        return new DashboardDto
        {
            TotalCases = summary.TotalCases,
            OpenCases = summary.OpenCases,
            ClosedCases = summary.ClosedCases,
            ResolvedCases = summary.ResolvedCases,
            AiPredictedCases = summary.AiPredictedCases,
            HighPriorityCases = summary.HighPriorityCases,
            TotalCustomers = summary.TotalCustomers,
            ByStatus = summary.ByStatus,
            ByPriority = summary.ByPriority,
            Trend = trend.Select(t => new DateCountDto
            {
                Date = t.Date.ToString("yyyy-MM-dd"),
                Count = t.Count,
            }).ToList(),
            ByCategory = byCategory.Select(c => new CategoryCountDto
            {
                Category = c.Category,
                Count = c.Count,
            }).ToList(),
            OverdueFollowUps = summary.OverdueFollowUps,
            OverdueFollowUpsList = summary.OverdueFollowUpDetails.Select(o => new OverdueFollowUpDto
            {
                CaseId = o.CaseId,
                Subject = o.Subject,
                CustomerName = o.CustomerName,
                AssignedToUserName = o.AssignedToUserName,
                Priority = o.Priority,
                FollowUpDueUtc = o.FollowUpDueUtc,
                DaysOverdue = o.DaysOverdue,
            }).ToList(),
            RecentCases = recent.Select(c => new RecentCaseDto
            {
                Id = c.Id,
                Subject = c.Subject,
                CustomerName = c.Customer?.Name ?? string.Empty,
                CategoryName = c.Category?.Name ?? string.Empty,
                CreatedAtUtc = c.CreatedAtUtc,
                Priority = c.Priority,
                Status = c.Status,
                PriorityAutoSuggested = c.PriorityAutoSuggested,
            }).ToList(),
        };
    }
}
