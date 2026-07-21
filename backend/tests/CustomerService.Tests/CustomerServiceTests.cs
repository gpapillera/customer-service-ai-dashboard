using CustomerService.Application.Dtos;
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
    private static CustomerService.Application.Services.CustomerService BuildService(out FakeRepository<Customer> customers, out FakeRepository<Case> cases)
    {
        customers = new FakeRepository<Customer>();
        cases = new FakeRepository<Case>();
        return new CustomerService.Application.Services.CustomerService(customers, cases);
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
}
