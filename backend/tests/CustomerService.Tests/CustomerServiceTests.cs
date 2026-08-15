using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.Tests.Fakes;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for <see cref="CustomerService"/>, focused on Phase 6 Agent
/// scoping (list / get-by-id / case-history restrictions).
/// </summary>
public class CustomerServiceTests
{
    private static CustomerService.Application.Services.CustomerService BuildService(
        out FakeRepository<Customer> customers, out FakeRepository<Case> cases, out FakeRepository<CustomerActivity> activities)
    {
        customers = new FakeRepository<Customer>();
        cases = new FakeRepository<Case>();
        var notifications = new FakeRepository<Notification>();
        activities = new FakeRepository<CustomerActivity>();
        var displayIds = new CustomerDisplayIdGenerator();
        var viewEvents = new FakeViewEventService();
        return new CustomerService.Application.Services.CustomerService(customers, cases, notifications, activities, displayIds, viewEvents);
    }

    // Back-compat overload for tests that don't exercise the activity audit.
    private static CustomerService.Application.Services.CustomerService BuildService(
        out FakeRepository<Customer> customers, out FakeRepository<Case> cases)
    {
        return BuildService(out customers, out cases, out var _);
    }

    private static Customer SeedCustomer(FakeRepository<Customer> repo, int id, string name = "Cust")
    {
        var c = new Customer { Id = id, Name = name, Email = $"c{id}@e.com" };
        (repo as IRepository<Customer>).AddAsync(c).Wait();
        return c;
    }

    private static Case SeedCase(FakeRepository<Case> repo, int id, int customerId, string? assignedTo)
    {
        var c = new Case
        {
            Id = id,
            Subject = $"Case {id}",
            CustomerId = customerId,
            CategoryId = 1,
            Status = CaseStatus.New,
            Priority = Priority.Low,
            AssignedToUserId = assignedTo,
        };
        (repo as IRepository<Case>).AddAsync(c).Wait();
        return c;
    }

    [Fact]
    public async Task GetAllAsync_AgentSeesOnlyCustomersWithSharedCase()
    {
        var svc = BuildService(out var customers, out var cases);
        SeedCustomer(customers, 1, "Alpha");
        SeedCustomer(customers, 2, "Beta");
        SeedCase(cases, 1, 1, "agent-001"); // shared with agent-001
        SeedCase(cases, 2, 2, "agent-002"); // not shared

        var adminView = await svc.GetAllAsync();
        Assert.Equal(2, adminView.Count);

        var agentView = await svc.GetAllAsync("Agent", "agent-001");
        Assert.Single(agentView);
        Assert.Equal("Alpha", agentView[0].Name);
    }

    [Fact]
    public async Task GetByIdAsync_AgentWithoutSharedCase_ThrowsForbidden()
    {
        var svc = BuildService(out var customers, out var cases);
        SeedCustomer(customers, 1, "Alpha");
        SeedCase(cases, 1, 1, "agent-002");

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetByIdAsync(1, "Agent", "agent-001"));
    }

    [Fact]
    public async Task GetCustomerCaseHistoryAsync_AgentSeesOnlyOwnCases()
    {
        var svc = BuildService(out var customers, out var cases);
        SeedCustomer(customers, 1, "Alpha");
        SeedCase(cases, 1, 1, "agent-001");
        SeedCase(cases, 2, 1, "agent-002");

        var adminHistory = await svc.GetCustomerCaseHistoryAsync(1);
        Assert.Equal(2, adminHistory.Count);

        var agentHistory = await svc.GetCustomerCaseHistoryAsync(1, "Agent", "agent-001");
        Assert.Single(agentHistory);
        Assert.Equal(1, agentHistory[0].Id);
    }

    [Fact]
    public async Task UpdateAsync_ProfileEdit_WritesAccountActivityRow()
    {
        var svc = BuildService(out var customers, out _, out var activities);
        SeedCustomer(customers, 1, "Alpha");

        await svc.UpdateAsync(new UpdateCustomerDto { Id = 1, Name = "Alpha Updated", Email = "c1@e.com" }, "Admin", "admin-001");

        var rows = activities.Query().ToList();
        Assert.Single(rows);
        Assert.Equal(1, rows[0].CustomerId);
        Assert.Equal("account_updated", rows[0].Kind);
        Assert.Equal("Profile updated", rows[0].Label);
        Assert.Equal("Admin", rows[0].ActorRole);
        Assert.Equal("admin-001", rows[0].ActorUserId);
        Assert.Contains("name", rows[0].Detail);
    }

    [Fact]
    public async Task UpdateAsync_NoChange_WritesNoActivityRow()
    {
        var svc = BuildService(out var customers, out _, out var activities);
        SeedCustomer(customers, 1, "Alpha");

        // Saving identical values must NOT record an audit row.
        await svc.UpdateAsync(new UpdateCustomerDto { Id = 1, Name = "Alpha", Email = "c1@e.com" }, "Admin", "admin-001");

        Assert.Empty(activities.Query().ToList());
    }

    [Fact]
    public async Task GetCustomerActivityAsync_IncludesProfileEdit()
    {
        var svc = BuildService(out var customers, out var cases, out var activities);
        var c = SeedCustomer(customers, 1, "Alpha");
        var edit = new CustomerActivity
        {
            Id = 1,
            CustomerId = 1,
            Kind = "account_updated",
            Label = "Profile updated",
            Detail = "Changed: name",
            AtUtc = DateTime.UtcNow,
            ActorRole = "Admin",
        };
        (activities as IRepository<CustomerActivity>).AddAsync(edit).Wait();

        var items = await svc.GetCustomerActivityAsync(1);

        var auditRow = items.FirstOrDefault(i => i.Kind == "account_updated");
        Assert.NotNull(auditRow);
        Assert.Equal("Profile updated", auditRow!.Label);
        Assert.Equal("Changed: name", auditRow.Detail);
        Assert.Null(auditRow.CaseId);
    }

    [Fact]
    public async Task GetByIdAsync_ProfileEdit_UpdatesLastActivity()
    {
        var svc = BuildService(out var customers, out var cases, out var activities);
        SeedCustomer(customers, 1, "Alpha");
        var edit = new CustomerActivity
        {
            Id = 1,
            CustomerId = 1,
            Kind = "account_updated",
            Label = "Profile updated",
            Detail = "Changed: name",
            AtUtc = DateTime.UtcNow,
        };
        (activities as IRepository<CustomerActivity>).AddAsync(edit).Wait();

        var dto = await svc.GetByIdAsync(1);

        Assert.Equal("Profile updated", dto!.LastActivityDescription);
        Assert.Null(dto.LastActivityCaseId);
    }
}
