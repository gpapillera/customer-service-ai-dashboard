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

    [Fact]
    public async Task DeleteAsync_AsAdmin_SoftDeletesCustomerAndCascadesToCases()
    {
        var svc = BuildService(out var customers, out var cases);
        var customer = SeedCustomer(customers, 1, "Alpha");
        var case1 = SeedCase(cases, 1, 1, "agent-001");
        var case2 = SeedCase(cases, 2, 1, "agent-002");

        // The in-memory fake does not populate navigation properties the way
        // EF's Include does, so wire the graph manually to mirror it.
        customer.Cases = new List<Case> { case1, case2 };

        await svc.DeleteAsync(1, callerRole: "Admin", callerUserId: "admin-001");

        // Customer and both cases are soft-deleted...
        Assert.True(customer.IsDeleted);
        Assert.True(case1.IsDeleted);
        Assert.True(case2.IsDeleted);
        Assert.Equal("admin-001", customer.DeletedById);
        Assert.Equal("admin-001", case1.DeletedById);
        Assert.Equal("admin-001", case2.DeletedById);

        // ...but the rows still exist in the store (no physical removal).
        Assert.Equal(1, customers.Query().Count());
        Assert.Equal(2, cases.Query().Count());
    }

    [Fact]
    public async Task RestoreAsync_SelectedCase_RestoresCustomerAndOneCaseOnly()
    {
        var svc = BuildService(out var customers, out var cases);
        var customer = SeedCustomer(customers, 1, "Alpha");
        var case1 = SeedCase(cases, 1, 1, "agent-001");
        var case2 = SeedCase(cases, 2, 1, "agent-002");

        // The in-memory fake does not populate navigation properties the way
        // EF's Include does, so wire the graph manually to mirror it. This
        // must be set BEFORE DeleteAsync so the cascade soft-deletes the cases.
        customer.Cases = new List<Case> { case1, case2 };

        // Soft-delete the customer (cascades to both cases).
        await svc.DeleteAsync(1, callerRole: "Admin", callerUserId: "admin-001");

        // Restore the customer but only case1.
        await svc.RestoreAsync(1, new List<int> { case1.Id }, callerUserId: "admin-001");

        // Customer is back...
        Assert.False(customer.IsDeleted);
        Assert.Null(customer.DeletedAtUtc);
        Assert.Null(customer.DeletedById);
        Assert.Equal("admin-001", customer.RestoredById);

        // ...case1 restored, case2 still binned...
        Assert.False(case1.IsDeleted);
        Assert.True(case2.IsDeleted);

        // ...and every row still exists in the store (no physical removal).
        Assert.Equal(1, customers.Query().Count());
        Assert.Equal(2, cases.Query().Count());
    }

    // ---- Task A8: PurgeAsync (keep-row anonymize / GDPR erasure) ----

    private static CustomerService.Application.Services.CustomerService BuildServiceWithNotifications(
        out FakeRepository<Customer> customers, out FakeRepository<Case> cases, out FakeRepository<Notification> notifications)
    {
        customers = new FakeRepository<Customer>();
        cases = new FakeRepository<Case>();
        notifications = new FakeRepository<Notification>();
        var activities = new FakeRepository<CustomerActivity>();
        var displayIds = new CustomerDisplayIdGenerator();
        var viewEvents = new FakeViewEventService();
        return new CustomerService.Application.Services.CustomerService(customers, cases, notifications, activities, displayIds, viewEvents);
    }

    [Fact]
    public async Task PurgeAsync_AsAdmin_KeepsRowAndAnonymizesPii()
    {
        var svc = BuildServiceWithNotifications(out var customers, out var cases, out var notifications);
        var customer = SeedCustomer(customers, 1, "Alpha");
        customer.Email = "victim@x.com";
        customer.Phone = "555-1212";
        customer.Company = "Acme";
        customer.Address = "1 Main St";
        customer.Account = new CustomerAccount { Id = 1, CustomerId = 1, PasswordHash = "bcrypt-hash", IsActive = true };

        var case1 = SeedCase(cases, 1, 1, "agent-001");
        var comment = new CaseComment { Id = 1, CaseId = 1, AuthorCustomerId = 1, Body = "My issue is urgent" };
        case1.Comments = new List<CaseComment> { comment };
        customer.Cases = new List<Case> { case1 };

        var note = new Notification { Id = 1, Recipient = "victim@x.com", Type = NotificationType.CustomerInvite };
        (notifications as IRepository<Notification>).AddAsync(note).Wait();

        // Soft-delete first (cascades to case + nullifies comment authorship).
        await svc.DeleteAsync(1, callerRole: "Admin", callerUserId: "admin-001");

        // Admin hard-purges: keep the row but erase PII.
        await svc.PurgeAsync(1, callerRole: "Admin");

        // Row still exists (no physical delete)...
        Assert.Equal(1, customers.Query().Count());

        // ...profile PII scrubbed...
        Assert.Equal("Deleted User", customer.Name);
        Assert.Equal(string.Empty, customer.Email);
        Assert.Null(customer.Phone);
        Assert.Null(customer.Company);
        Assert.Null(customer.Address);

        // ...account credentials disabled so the login can never be reused...
        Assert.NotNull(customer.Account);
        Assert.Null(customer.Account!.PasswordHash);
        Assert.False(customer.Account.IsActive);

        // ...purge markers set...
        Assert.True(customer.Purged);
        Assert.NotNull(customer.PurgedAtUtc);

        // ...comment text preserved but authorship link nulled...
        Assert.Equal("My issue is urgent", comment.Body);
        Assert.Null(comment.AuthorCustomerId);

        // ...notification recipient scrubbed...
        Assert.Null(note.Recipient);

        // ...and it no longer appears in the recycle bin (IsDeleted && !Purged).
        Assert.Empty(customers.Query().Where(c => c.IsDeleted && !c.Purged).ToList());
    }

    [Fact]
    public async Task PurgeAsync_NonAdmin_ThrowsForbidden()
    {
        var svc = BuildServiceWithNotifications(out var customers, out var cases, out var _);
        var customer = SeedCustomer(customers, 1, "Alpha");
        customer.Cases = new List<Case>();

        await svc.DeleteAsync(1, callerRole: "Admin", callerUserId: "admin-001");

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.PurgeAsync(1, callerRole: "Agent"));
    }

    [Fact]
    public async Task PurgeAsync_NotInRecycleBin_ThrowsKeyNotFound()
    {
        var svc = BuildServiceWithNotifications(out var customers, out var cases, out var _);
        SeedCustomer(customers, 1, "Alpha");

        // Never soft-deleted -> not in the recycle bin.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.PurgeAsync(1, callerRole: "Admin"));
    }

    [Fact]
    public async Task GetDeletedAsync_ReturnsBinnedOnly_ExcludesPurged()
    {
        var svc = BuildService(out var customers, out var cases, out var _);
        var customer = SeedCustomer(customers, 1, "Alpha");
        var c1 = SeedCase(cases, 1, 1, "agent-001");
        var c2 = SeedCase(cases, 2, 1, "agent-002");
        customer.Cases = new List<Case> { c1, c2 };

        // Soft-delete the customer (cascades to cases).
        await svc.DeleteAsync(1, callerRole: "Admin", callerUserId: "admin-001");
        // Purge the customer so it leaves the bin.
        await svc.PurgeAsync(1, callerRole: "Admin");

        // A fresh, still-binned customer.
        var customer2 = SeedCustomer(customers, 2, "Bravo");
        customer2.Cases = new List<Case>();
        await svc.DeleteAsync(2, callerRole: "Admin", callerUserId: "admin-001");

        var binned = await svc.GetDeletedAsync();

        // Only the still-binned customer is returned; the purged one is excluded.
        Assert.Single(binned);
        Assert.Equal("Bravo", binned[0].Name);
        Assert.True(binned[0].IsDeleted);
        Assert.NotNull(binned[0].DeletedAtUtc);
    }
}
