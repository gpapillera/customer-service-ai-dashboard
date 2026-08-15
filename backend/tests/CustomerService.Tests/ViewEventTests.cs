using CustomerService.Application.Dtos;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Tests.Fakes;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for <see cref="ViewEventService"/> — the viewed/opened audit.
/// Focus: the 10-minute per-viewer cooldown (so refreshes don't flood the log)
/// and the customer-scoped query (account views + that customer's case views).
/// </summary>
public class ViewEventTests
{
    private static ViewEventService Build(out FakeRepository<ViewEvent> repo)
    {
        repo = new FakeRepository<ViewEvent>();
        return new ViewEventService(repo);
    }

    [Fact]
    public async Task RecordViewAsync_creates_row_on_first_open()
    {
        var svc = Build(out var repo);
        var created = await svc.RecordViewAsync("Case", 5, "u1", "Ada Admin", "Admin");

        Assert.NotNull(created);
        Assert.Equal("Case", created!.TargetType);
        Assert.Equal(5, created.TargetId);
        Assert.Equal("u1", created.ViewerUserId);
        Assert.Equal("Ada Admin", created.ViewerName);
        Assert.Single(repo.Query());
    }

    [Fact]
    public async Task RecordViewAsync_does_not_duplicate_within_cooldown()
    {
        var svc = Build(out var repo);
        var baseTime = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        await svc.RecordViewAsync("Case", 5, "u1", "Ada Admin", "Admin", now: baseTime);

        // Second open 2 minutes later by the same viewer is coalesced.
        var second = await svc.RecordViewAsync("Case", 5, "u1", "Ada Admin", "Admin", now: baseTime.AddMinutes(2));

        Assert.Null(second);
        Assert.Single(repo.Query()); // still exactly one row
    }

    [Fact]
    public async Task RecordViewAsync_records_again_after_cooldown()
    {
        var svc = Build(out var repo);
        var baseTime = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        await svc.RecordViewAsync("Case", 5, "u1", "Ada Admin", "Admin", now: baseTime);

        // 11 minutes later by the same viewer is a new row.
        var second = await svc.RecordViewAsync("Case", 5, "u1", "Ada Admin", "Admin", now: baseTime.AddMinutes(11));

        Assert.NotNull(second);
        Assert.Equal(2, repo.Query().Count());
    }

    [Fact]
    public async Task RecordViewAsync_different_viewers_each_record()
    {
        var svc = Build(out var repo);
        await svc.RecordViewAsync("Case", 5, "u1", "Ada Admin", "Admin");
        var u2 = await svc.RecordViewAsync("Case", 5, "u2", "Ben Agent", "Agent");

        Assert.NotNull(u2);
        Assert.Equal(2, repo.Query().Count());
    }

    [Fact]
    public async Task GetForCustomerAsync_returns_account_and_case_views_newest_first()
    {
        var svc = Build(out _);
        var baseTime = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        // Account view (older, 5 min ago).
        await svc.RecordViewAsync("Customer", 10, "u1", "Ada Admin", "Admin", now: baseTime.AddMinutes(5));
        // Case view for one of this customer's cases (newer, 40 min ago).
        await svc.RecordViewAsync("Case", 42, "u1", "Ada Admin", "Admin", now: baseTime.AddMinutes(40));

        var items = await svc.GetForCustomerAsync(10, new[] { 42 });

        Assert.Equal(2, items.Count);
        // Newest first: the case view (5 min ago) precedes the account view (30 min ago).
        Assert.Equal("Case", items[0].TargetType);
        Assert.Equal(42, items[0].TargetId);
        Assert.Equal("Customer", items[1].TargetType);
        Assert.Equal(10, items[1].TargetId);
    }

    [Fact]
    public async Task GetForCustomerAsync_excludes_other_customers_cases()
    {
        var svc = Build(out _);
        await svc.RecordViewAsync("Customer", 10, "u1", "Ada Admin", "Admin");
        await svc.RecordViewAsync("Case", 999, "u1", "Ada Admin", "Admin"); // another customer's case

        var items = await svc.GetForCustomerAsync(10, new[] { 42 });

        Assert.Single(items); // only the account view; case 999 is not in the scope
        Assert.Equal("Customer", items[0].TargetType);
    }
}
