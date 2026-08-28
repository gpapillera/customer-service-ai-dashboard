using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.Tests.Fakes;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Locks in the customer self-service profile edit audit: a customer editing
/// their own profile (email is intentionally NOT editable here) must write an
/// <c>account_updated</c> row attributed to <c>ActorRole = "Customer"</c>, and a
/// no-op save must write nothing. Mirrors the staff-edit assertions in
/// <see cref="CustomerServiceTests"/>.
/// </summary>
public class CustomerProfileAuditTests
{
    private static CustomerAuthService BuildService(out FakeRepository<Customer> customers, out FakeRepository<CustomerActivity> activities, out FakeLiveUpdateHub hub)
    {
        customers = new FakeRepository<Customer>();
        var accounts = new FakeRepository<CustomerAccount>();
        activities = new FakeRepository<CustomerActivity>();
        // UpdateProfileAsync never sends email, so a no-op sender stub is fine.
        INotificationSender sender = new StubNotificationSender();
        IRefreshTokenService refreshTokens = new StubRefreshTokenService();
        hub = new FakeLiveUpdateHub();
        return new CustomerAuthService(customers, accounts, activities, sender, null!, new CustomerDisplayIdGenerator(), refreshTokens, hub);
    }

    [Fact]
    public async Task UpdateProfileAsync_ChangingFields_WritesCustomerAttributedRow()
    {
        var svc = BuildService(out var customers, out var activities, out var hub);
        var c = new Customer { Id = 1, Name = "Maria", Email = "maria@example.com" };
        await (customers as IRepository<Customer>).AddAsync(c);

        await svc.UpdateProfileAsync(1, new UpdateCustomerProfileDto { Name = "Maria Edited", Phone = null, Company = null, Address = null });

        var rows = activities.Query().ToList();
        Assert.Single(rows);
        Assert.Equal(1, rows[0].CustomerId);
        Assert.Equal("account_updated", rows[0].Kind);
        Assert.Equal("Profile updated", rows[0].Label);
        Assert.Equal("Customer", rows[0].ActorRole);
        Assert.Contains("name", rows[0].Detail);

        // The self-service edit must ALSO push a "customer-update" live event
        // (so the agent grid + sidenav badge reflect the edit without a manual
        // refresh) AND stamp UpdatedAtUtc (which the badge counts on). This is
        // the fix for "editing my profile at /customer/ doesn't update agents".
        var evt = Assert.Single(hub.Published());
        Assert.Equal("customer-update", evt.Kind);
        Assert.Equal(1, evt.CustomerId);
        Assert.Equal("Customer", evt.ActorRole);
        Assert.Equal("1", evt.ActorUserId);
        // UpdatedAtUtc must be set so the nav badge's "updated since visit" trips.
        Assert.NotNull(c.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateProfileAsync_NoChange_WritesNoRow()
    {
        var svc = BuildService(out var customers, out var activities, out var hub);
        var c = new Customer { Id = 1, Name = "Maria", Email = "maria@example.com" };
        await (customers as IRepository<Customer>).AddAsync(c);

        await svc.UpdateProfileAsync(1, new UpdateCustomerProfileDto { Name = "Maria", Phone = null, Company = null, Address = null });

        Assert.Empty(activities.Query().ToList());
        // A no-op save must NOT push a live event (and must NOT bump UpdatedAtUtc).
        Assert.Empty(hub.Published());
        Assert.Null(c.UpdatedAtUtc);
    }
}

/// <summary>No-op <see cref="INotificationSender"/> for tests that never trigger a send.</summary>
public sealed class StubNotificationSender : INotificationSender
{
    public Task SendAsync(Notification notification) => Task.CompletedTask;
}

/// <summary>No-op <see cref="IRefreshTokenService"/> for tests that never exercise refresh/login.</summary>
public sealed class StubRefreshTokenService : IRefreshTokenService
{
    public Task<string> CreateAsync(string subjectId, string subjectType, string role, int daysValid) =>
        Task.FromResult("stub-refresh-token");
    public Task<(bool Ok, RefreshToken? Token)> ValidateAsync(string token) =>
        Task.FromResult<(bool, RefreshToken?)>((false, null));
    public Task<string> RotateAsync(string oldToken) => Task.FromResult("stub-rotated");
    public Task RevokeAsync(string token) => Task.CompletedTask;
}
