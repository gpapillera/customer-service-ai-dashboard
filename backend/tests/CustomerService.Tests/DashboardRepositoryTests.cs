using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.Infrastructure.Data;
using CustomerService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for <see cref="DashboardRepository.GetOverdueFollowUpsAsync"/>.
/// Uses the EF Core InMemory provider so the overdue-detection rule (open case,
/// past deadline, no follow-up since the deadline) is exercised against a real
/// query pipeline.
///
/// Note: the InMemory provider drops rows whose included navigation (Customer)
/// has no matching principal, so each test seeds a Customer with the same id.
/// </summary>
public class DashboardRepositoryTests
{
    private static AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedCustomer(AppDbContext ctx, int id = 1)
    {
        ctx.Customers.Add(new Customer { Id = id, Name = $"Customer {id}", Email = $"c{id}@e.com" });
    }

    private static Case MakeCase(int id, CaseStatus status, DateTime? due, int customerId = 1)
    {
        return new Case
        {
            Id = id,
            Subject = $"Case {id}",
            CustomerId = customerId,
            Status = status,
            Priority = Priority.Medium,
            FollowUpDueUtc = due,
        };
    }

    [Fact]
    public async Task GetOverdueFollowUpsAsync_ReturnsOpenCaseWithPastDeadline()
    {
        using var ctx = BuildContext();
        SeedCustomer(ctx, 1);
        ctx.Cases.Add(MakeCase(1, CaseStatus.InProgress, DateTime.UtcNow.AddDays(-3)));
        await ctx.SaveChangesAsync();

        var repo = new DashboardRepository(ctx);
        var overdue = await repo.GetOverdueFollowUpsAsync();

        Assert.Single(overdue);
        Assert.Equal(1, overdue[0].CaseId);
        Assert.True(overdue[0].DaysOverdue >= 3);
    }

    [Fact]
    public async Task GetOverdueFollowUpsAsync_ExcludesClosedCases()
    {
        using var ctx = BuildContext();
        SeedCustomer(ctx, 1);
        ctx.Cases.Add(MakeCase(1, CaseStatus.Closed, DateTime.UtcNow.AddDays(-3)));
        await ctx.SaveChangesAsync();

        var repo = new DashboardRepository(ctx);
        var overdue = await repo.GetOverdueFollowUpsAsync();

        Assert.Empty(overdue);
    }

    [Fact]
    public async Task GetOverdueFollowUpsAsync_ExcludesFutureDeadlines()
    {
        using var ctx = BuildContext();
        SeedCustomer(ctx, 1);
        // Future deadline AND recently created (not stale) → not overdue.
        var c = MakeCase(1, CaseStatus.InProgress, DateTime.UtcNow.AddDays(3));
        c.CreatedAtUtc = DateTime.UtcNow.AddHours(-1);
        ctx.Cases.Add(c);
        await ctx.SaveChangesAsync();

        var repo = new DashboardRepository(ctx);
        var overdue = await repo.GetOverdueFollowUpsAsync();

        Assert.Empty(overdue);
    }

    [Fact]
    public async Task GetOverdueFollowUpsAsync_FlagsStaleOpenCaseWithNoDeadline()
    {
        using var ctx = BuildContext();
        SeedCustomer(ctx, 1);
        // Open, no deadline, no follow-up for longer than the stale threshold.
        var c = MakeCase(1, CaseStatus.New, null);
        c.CreatedAtUtc = DateTime.UtcNow.AddDays(-5);
        ctx.Cases.Add(c);
        await ctx.SaveChangesAsync();

        var repo = new DashboardRepository(ctx);
        var overdue = await repo.GetOverdueFollowUpsAsync();

        Assert.Single(overdue);
        Assert.Equal(1, overdue[0].CaseId);
    }

    [Fact]
    public async Task GetOverdueFollowUpsAsync_ExcludesCasesFollowedUpSinceDeadline()
    {
        using var ctx = BuildContext();
        SeedCustomer(ctx, 1);
        var due = DateTime.UtcNow.AddDays(-3);
        var c = MakeCase(1, CaseStatus.InProgress, due);
        c.CallLogs = new List<CallLog>
        {
            new CallLog { Id = 1, CaseId = 1, CreatedAtUtc = due.AddHours(2), Notes = "Followed up" },
        };
        ctx.Cases.Add(c);
        await ctx.SaveChangesAsync();

        var repo = new DashboardRepository(ctx);
        var overdue = await repo.GetOverdueFollowUpsAsync();

        Assert.Empty(overdue);
    }

    [Fact]
    public async Task GetOverdueFollowUpsAsync_SortsMostOverdueFirst()
    {
        using var ctx = BuildContext();
        SeedCustomer(ctx, 1);
        ctx.Cases.Add(MakeCase(1, CaseStatus.InProgress, DateTime.UtcNow.AddDays(-1)));
        ctx.Cases.Add(MakeCase(2, CaseStatus.InProgress, DateTime.UtcNow.AddDays(-10)));
        ctx.Cases.Add(MakeCase(3, CaseStatus.New, DateTime.UtcNow.AddDays(-5)));
        await ctx.SaveChangesAsync();

        var repo = new DashboardRepository(ctx);
        var overdue = await repo.GetOverdueFollowUpsAsync();

        Assert.Equal(3, overdue.Count);
        Assert.Equal(2, overdue[0].CaseId); // 10 days overdue first
        Assert.Equal(3, overdue[1].CaseId);
        Assert.Equal(1, overdue[2].CaseId);
    }
}
