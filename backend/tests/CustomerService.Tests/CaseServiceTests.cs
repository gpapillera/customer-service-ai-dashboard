using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;
using CustomerService.Domain;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.ML;
using Microsoft.EntityFrameworkCore;
using CustomerService.Tests.Fakes;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for <see cref="CaseService"/>. The repository and predictor are
/// faked so the service logic (filtering, ML auto-suggestion, validation,
/// not-found handling) is exercised in isolation.
/// </summary>
public class CaseServiceTests
{
    private static CaseService BuildService(
        out FakeRepository<Case> cases,
        out FakeRepository<Customer> customers,
        out FakeRepository<Category> categories,
        IPriorityPredictor? predictor = null)
    {
        cases = new FakeRepository<Case>();
        customers = new FakeRepository<Customer>();
        categories = new FakeRepository<Category>();
        var comments = new FakeRepository<CaseComment>();
        var readStates = new FakeRepository<ConversationReadState>();
        var activities = new FakeRepository<CustomerActivity>();
        predictor ??= new RuleBasedPriorityPredictor();
        INotificationService notifications = new FakeNotificationService();
        ICaseEventHub events = new FakeCaseEventHub();
        return new CaseService(cases, customers, categories, comments, readStates, activities, predictor, notifications, events);
    }

    private static Customer SeedCustomer(FakeRepository<Customer> repo, int id = 1)
    {
        var c = new Customer { Id = id, Name = "Test Customer", Email = "t@e.com" };
        // FakeRepository.AddAsync assigns Id; here we set explicitly for control.
        typeof(Customer).GetProperty("Id")!.SetValue(c, id);
        repo.Query().ToList(); // no-op to keep reference
        (repo as IRepository<Customer>).AddAsync(c).Wait();
        return c;
    }

    private static Category SeedCategory(FakeRepository<Category> repo, int id = 1)
    {
        var c = new Category { Id = id, Name = "Billing" };
        (repo as IRepository<Category>).AddAsync(c).Wait();
        return c;
    }

    [Fact]
    public async Task CreateAsync_WithoutPriority_UsesMlSuggestion_AndFlagsAutoSuggested()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var dto = new CreateCaseDto
        {
            Subject = "Double billed",
            Description = "URGENT refund needed",
            CustomerId = 1,
            CategoryId = 1,
        };

        var created = await svc.CreateAsync(dto);

        Assert.Equal("Double billed", created.Subject);
        Assert.True(created.PriorityAutoSuggested);
        Assert.False(string.IsNullOrWhiteSpace(created.PriorityReason));
        Assert.NotEqual(0, created.Id);
    }

    [Fact]
    public async Task CreateAsync_WithExplicitPriority_DoesNotFlagAutoSuggested()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var dto = new CreateCaseDto
        {
            Subject = "Routine question",
            Description = "How do I reset my password?",
            CustomerId = 1,
            CategoryId = 1,
            Priority = Priority.Low,
        };

        var created = await svc.CreateAsync(dto);

        Assert.Equal(Priority.Low, created.Priority);
        Assert.False(created.PriorityAutoSuggested);
        Assert.Null(created.PriorityReason);
    }

    [Fact]
    public async Task CreateAsync_UnknownCustomer_ThrowsKeyNotFoundException()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCategory(categories, 1);

        var dto = new CreateCaseDto { Subject = "x", CustomerId = 999, CategoryId = 1 };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.CreateAsync(dto));
    }

    [Fact]
    public async Task CreateAsync_UnknownCategory_ThrowsKeyNotFoundException()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);

        var dto = new CreateCaseDto { Subject = "x", CustomerId = 1, CategoryId = 999 };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.CreateAsync(dto));
    }

    [Fact]
    public async Task GetAllAsync_FiltersByStatus()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        await svc.CreateAsync(new CreateCaseDto { Subject = "A", CustomerId = 1, CategoryId = 1 });
        await svc.CreateAsync(new CreateCaseDto { Subject = "B", CustomerId = 1, CategoryId = 1 });

        var all = await svc.GetAllAsync(null, null, null, null, null);
        Assert.Equal(2, all.Count);

        var filtered = await svc.GetAllAsync(CaseStatus.New, null, null, null, null);
        Assert.Equal(2, filtered.Count); // both default to New

        var none = await svc.GetAllAsync(CaseStatus.Closed, null, null, null, null);
        Assert.Empty(none);
    }

    [Fact]
    public async Task UpdateAsync_OverridesPriority_AndClearsAutoSuggested()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var created = await svc.CreateAsync(new CreateCaseDto
        {
            Subject = "A",
            Description = "urgent issue",
            CustomerId = 1,
            CategoryId = 1,
        });
        Assert.True(created.PriorityAutoSuggested);

        await svc.UpdateAsync(created.Id, new UpdateCaseDto
        {
            Subject = "A",
            Description = "urgent issue",
            Status = CaseStatus.InProgress,
            Priority = Priority.High,
            CategoryId = 1,
        });

        var updated = await svc.GetByIdAsync(created.Id);
        Assert.Equal(Priority.High, updated!.Priority);
        Assert.Equal(CaseStatus.InProgress, updated.Status);
        Assert.False(updated.PriorityAutoSuggested);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesCase_KeepsRowAndFlags()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        var customer = SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var created = await svc.CreateAsync(new CreateCaseDto { Subject = "A", CustomerId = 1, CategoryId = 1 });
        Assert.NotNull(await svc.GetByIdAsync(created.Id));

        // Admin soft-deletes the case.
        await svc.DeleteAsync(created.Id, callerRole: "Admin", callerUserId: "admin-1");

        // Row still exists — soft delete, no physical removal.
        var stored = cases.Query().FirstOrDefault(c => c.Id == created.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);
        Assert.NotNull(stored.DeletedAtUtc);
        Assert.True(stored.DeletedAtUtc.HasValue);
        Assert.Equal("admin-1", stored.DeletedById);

        // The customer is NOT touched by a case soft-delete.
        var storedCustomer = customers.Query().FirstOrDefault(c => c.Id == 1);
        Assert.NotNull(storedCustomer);
        Assert.Equal(customer.Name, storedCustomer!.Name);
        Assert.False(storedCustomer.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteAsync(123, callerRole: "Admin"));
    }

    /// <summary>No-op notification service for CaseService tests.</summary>
    private class FakeNotificationService : INotificationService
    {
        public Task<int> GenerateOverdueAsync() => Task.FromResult(0);
        public Task<int> NotifyResolvedAsync(Case caseEntity) => Task.FromResult(0);
        public Task<int> NotifyNewCustomerMessageAsync(Case caseEntity, string customerName) => Task.FromResult(0);
        public Task<IReadOnlyList<NotificationDto>> GetEmailLogAsync() => Task.FromResult<IReadOnlyList<NotificationDto>>(Array.Empty<NotificationDto>());
        public Task<IReadOnlyList<NotificationDto>> GetAllAsync(string? recipientUserId = null) => Task.FromResult<IReadOnlyList<NotificationDto>>(Array.Empty<NotificationDto>());
        public Task<NotificationSummaryDto> GetSummaryAsync(string? recipientUserId = null) => Task.FromResult(new NotificationSummaryDto());
        public Task<bool> MarkReadAsync(int id) => Task.FromResult(false);
        public Task<int> MarkAllReadAsync() => Task.FromResult(0);
        public Task<NotificationDto> ComposeEmailAsync(ComposeEmailRequest request) =>
            Task.FromResult(new NotificationDto { Id = 1, Title = request.Subject, Message = request.Message, Channel = NotificationChannel.Email, Type = NotificationType.AdminManual, Status = NotificationStatus.Unread, CreatedAtUtc = DateTime.UtcNow, CaseId = request.CaseId, Recipient = request.Recipient });

        public Task<NotificationDto?> ResendEmailAsync(int id) =>
            Task.FromResult<NotificationDto?>(new NotificationDto { Id = id + 1000, Title = "Resent", Message = "Resent body", Channel = NotificationChannel.Email, Type = NotificationType.AdminManual, Status = NotificationStatus.Unread, CreatedAtUtc = DateTime.UtcNow });
    }

    /// <summary>No-op case-event hub for CaseService tests (collects nothing).</summary>
    private sealed class FakeCaseEventHub : ICaseEventHub
    {
        private readonly System.Threading.Channels.Channel<CaseEvent> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<CaseEvent>();
        public System.Threading.Channels.ChannelReader<CaseEvent> Reader => _channel.Reader;
        public ValueTask PublishAsync(CaseEvent evt) => _channel.Writer.WriteAsync(evt);
        public bool TryRead(out CaseEvent evt) => _channel.Reader.TryRead(out evt!);
    }

    // ---- Phase 6: Agent scoping ----

    [Fact]
    public async Task GetAllAsync_AgentSeesOnlyOwnAndUnassigned()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var mine = await svc.CreateAsync(new CreateCaseDto { Subject = "Mine", CustomerId = 1, CategoryId = 1 });
        var unassigned = await svc.CreateAsync(new CreateCaseDto { Subject = "None", CustomerId = 1, CategoryId = 1 });
        var others = await svc.CreateAsync(new CreateCaseDto { Subject = "Other", CustomerId = 1, CategoryId = 1 });

        // Assign: mine -> agent-001, others -> agent-002
        await svc.UpdateAsync(mine.Id, new UpdateCaseDto { Subject = "Mine", CategoryId = 1, AssignedToUserId = "agent-001" });
        await svc.UpdateAsync(others.Id, new UpdateCaseDto { Subject = "Other", CategoryId = 1, AssignedToUserId = "agent-002" });

        var agentView = await svc.GetAllAsync(null, null, null, null, null, false, null, "Agent", "agent-001");
        var ids = agentView.Select(c => c.Id).ToHashSet();
        Assert.Contains(mine.Id, ids);
        Assert.Contains(unassigned.Id, ids);
        Assert.DoesNotContain(others.Id, ids);
    }

    [Fact]
    public async Task GetByIdAsync_AgentCannotViewOthersCase_ThrowsForbidden()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var others = await svc.CreateAsync(new CreateCaseDto { Subject = "Other", CustomerId = 1, CategoryId = 1 });
        await svc.UpdateAsync(others.Id, new UpdateCaseDto { Subject = "Other", CategoryId = 1, AssignedToUserId = "agent-002" });

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetByIdAsync(others.Id, "Agent", "agent-001"));
    }

    [Fact]
    public async Task UpdateAsync_AgentCannotEditUnassigned_ThrowsForbidden()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var unassigned = await svc.CreateAsync(new CreateCaseDto { Subject = "None", CustomerId = 1, CategoryId = 1 });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.UpdateAsync(unassigned.Id, new UpdateCaseDto { Subject = "None", CategoryId = 1, Status = CaseStatus.InProgress }, "Agent", "agent-001"));
    }

    [Fact]
    public async Task UpdateAsync_AgentCannotReassign_ThrowsForbidden()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var mine = await svc.CreateAsync(new CreateCaseDto { Subject = "Mine", CustomerId = 1, CategoryId = 1 });
        await svc.UpdateAsync(mine.Id, new UpdateCaseDto { Subject = "Mine", CategoryId = 1, AssignedToUserId = "agent-001" });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.UpdateAsync(mine.Id, new UpdateCaseDto { Subject = "Mine", CategoryId = 1, AssignedToUserId = "agent-002" }, "Agent", "agent-001"));
    }

    [Fact]
    public async Task UpdateAsync_AgentCanEditOwnCase()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var mine = await svc.CreateAsync(new CreateCaseDto { Subject = "Mine", CustomerId = 1, CategoryId = 1 });
        await svc.UpdateAsync(mine.Id, new UpdateCaseDto { Subject = "Mine", CategoryId = 1, AssignedToUserId = "agent-001" });

        await svc.UpdateAsync(mine.Id, new UpdateCaseDto { Subject = "Mine", CategoryId = 1, Status = CaseStatus.InProgress }, "Agent", "agent-001");

        var updated = await svc.GetByIdAsync(mine.Id);
        Assert.Equal(CaseStatus.InProgress, updated!.Status);
    }

    [Fact]
    public async Task UpdateAsync_PublishesEvent_OnAssignChange()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);
        var hub = (FakeCaseEventHub)svc.GetType().GetField("_events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(svc)!;

        var c = await svc.CreateAsync(new CreateCaseDto { Subject = "X", CustomerId = 1, CategoryId = 1 });
        // Change assignee -> should publish one event with the new agent id.
        await svc.UpdateAsync(c.Id, new UpdateCaseDto { Subject = "X", CategoryId = 1, AssignedToUserId = "agent-001" });

        Assert.True(hub.TryRead(out var evt));
        Assert.Equal(c.Id, evt.CaseId);
        Assert.Equal("agent-001", evt.AssignedToUserId);
    }

    [Fact]
    public async Task UpdateAsync_PublishesEvent_OnUnassign()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);
        var hub = (FakeCaseEventHub)svc.GetType().GetField("_events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(svc)!;

        var c = await svc.CreateAsync(new CreateCaseDto { Subject = "X", CustomerId = 1, CategoryId = 1 });
        await svc.UpdateAsync(c.Id, new UpdateCaseDto { Subject = "X", CategoryId = 1, AssignedToUserId = "agent-001" });
        // Unassign -> event with null assignee (visible to both agents).
        await svc.UpdateAsync(c.Id, new UpdateCaseDto { Subject = "X", CategoryId = 1, AssignedToUserId = UpdateCaseDto.UnassignSentinel });

        Assert.True(hub.TryRead(out var assignEvt));
        Assert.Equal("agent-001", assignEvt.AssignedToUserId);
        Assert.True(hub.TryRead(out var unassignEvt));
        Assert.Null(unassignEvt.AssignedToUserId);
    }

    // ---- Task A2: soft-delete + purge fields (default state) ----

    [Fact]
    public void Case_DefaultState_HasSoftDeleteAndPurgeFieldsUnset()
    {
        var c = new Case { Subject = "X", CustomerId = 1 };

        Assert.False(c.IsDeleted);
        Assert.False(c.Purged);
        Assert.Null(c.DeletedAtUtc);
        Assert.Null(c.PurgedAtUtc);
    }

    // ---- Task A7: RestoreCaseAsync (account-gated) ----

    [Fact]
    public async Task RestoreCaseAsync_CustomerSoftDeleted_ThrowsInvalidOperationException()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        var customer = SeedCustomer(customers, 1);
        SeedCategory(categories, 1);
        var created = await svc.CreateAsync(new CreateCaseDto { Subject = "A", CustomerId = 1, CategoryId = 1 });

        // Admin soft-deletes the CUSTOMER. In the real DB this cascade
        // soft-deletes the case too; the fake has no cascade, so replicate it:
        // soft-delete the customer, soft-delete the case, and wire the navigation
        // the EF Include(c => c.Customer) would load so the gate can inspect it.
        customer.IsDeleted = true;
        customer.DeletedAtUtc = DateTime.UtcNow;
        customer.DeletedById = "admin-1";
        await svc.DeleteAsync(created.Id, "Admin", "admin-1");

        var stored = cases.Query().First(c => c.Id == created.Id);
        stored.Customer = customer; // simulate Include(c => c.Customer)

        // Gate: a case under a soft-deleted account cannot be restored.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RestoreCaseAsync(created.Id, "admin-1"));
    }

    [Fact]
    public async Task RestoreCaseAsync_CaseSoftDeletedCustomerActive_RestoresCase()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        var customer = SeedCustomer(customers, 1); // active
        SeedCategory(categories, 1);
        var created = await svc.CreateAsync(new CreateCaseDto { Subject = "A", CustomerId = 1, CategoryId = 1 });

        // Admin soft-deletes just the CASE (customer stays active).
        await svc.DeleteAsync(created.Id, "Admin", "admin-1");

        var stored = cases.Query().First(c => c.Id == created.Id);
        stored.Customer = customer; // active customer (gate passes)

        await svc.RestoreCaseAsync(created.Id, "admin-1");

        // Row still exists and is no longer soft-deleted.
        var restored = cases.Query().First(c => c.Id == created.Id);
        Assert.NotNull(restored);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAtUtc);
        Assert.Null(restored.DeletedById);
    }

    // ---- Task A9: PurgeCaseAsync (keep-row anonymize) ----

    [Fact]
    public async Task PurgeCaseAsync_AdminScrubbedCase_KeepsRowAndAnonymizes()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1); // active
        SeedCategory(categories, 1);

        var created = await svc.CreateAsync(new CreateCaseDto
        {
            Subject = "Real",
            Description = "Real desc",
            CustomerId = 1,
            CategoryId = 1,
        });

        // Admin soft-deletes (places it in the recycle bin).
        await svc.DeleteAsync(created.Id, "Admin", "admin-1");

        // Admin hard-purges: keep the row but anonymize it.
        await svc.PurgeCaseAsync(created.Id, "Admin");

        // Row still exists — no physical delete.
        var stored = cases.Query().IgnoreQueryFilters().FirstOrDefault(c => c.Id == created.Id);
        Assert.NotNull(stored);
        Assert.Equal("[deleted]", stored!.Subject);
        Assert.Equal("[deleted]", stored.Description);
        Assert.True(stored.Purged);
        Assert.True(stored.PurgedAtUtc.HasValue);
    }

    [Fact]
    public async Task PurgeCaseAsync_NonAdmin_ThrowsForbidden()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var created = await svc.CreateAsync(new CreateCaseDto
        {
            Subject = "Real",
            Description = "Real desc",
            CustomerId = 1,
            CategoryId = 1,
        });
        await svc.DeleteAsync(created.Id, "Admin", "admin-1");

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.PurgeCaseAsync(created.Id, "Agent"));
    }

    [Fact]
    public async Task PurgeCaseAsync_NotInRecycleBin_ThrowsKeyNotFound()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var created = await svc.CreateAsync(new CreateCaseDto
        {
            Subject = "Real",
            Description = "Real desc",
            CustomerId = 1,
            CategoryId = 1,
        });

        // Never soft-deleted -> not in the recycle bin.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.PurgeCaseAsync(created.Id, "Admin"));
    }

    [Fact]
    public async Task GetDeletedAsync_ReturnsBinnedOnly_ExcludesPurged_AndCarriesCustomerContext()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCategory(categories, 1);
        var cust = SeedCustomer(customers, 1);
        cust.Name = "Alpha";

        var binned = await svc.CreateAsync(new CreateCaseDto
        {
            Subject = "Stay in bin",
            Description = "d",
            CustomerId = 1,
            CategoryId = 1,
        });
        // Soft-delete just this case (customer stays active).
        await svc.DeleteAsync(binned.Id, "Admin", "admin-001");

        // A second case that we purge so it must be excluded from the bin.
        var purged = await svc.CreateAsync(new CreateCaseDto
        {
            Subject = "Will be purged",
            Description = "d",
            CustomerId = 1,
            CategoryId = 1,
        });
        await svc.DeleteAsync(purged.Id, "Admin", "admin-001");
        await svc.PurgeCaseAsync(purged.Id, "Admin");

        // The in-memory fake does not populate navigation properties via
        // Include (the way EF does), so wire the case -> customer link
        // manually to mirror it — otherwise c.Customer is null and the
        // GetDeletedAsync drawer context falls back to "Deleted User".
        foreach (var cs in cases.Query().ToList())
            cs.Customer = cust;

        var result = await svc.GetDeletedAsync();

        // Only the still-binned case is returned.
        Assert.Single(result);
        Assert.Equal(binned.Id, result[0].Id);
        Assert.True(result[0].IsDeleted);
        // Owning customer context is carried for the drawer.
        Assert.Equal("Alpha", result[0].CustomerName);
        Assert.False(result[0].CustomerIsDeleted); // account still active
        Assert.False(result[0].Purged);
    }
}
