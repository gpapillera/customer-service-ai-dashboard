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

        return new DashboardDto
        {
            TotalCases = summary.TotalCases,
            OpenCases = summary.OpenCases,
            ClosedCases = summary.ClosedCases,
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
        };
    }
}
