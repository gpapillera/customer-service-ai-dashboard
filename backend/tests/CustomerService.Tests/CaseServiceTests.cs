using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.ML;
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
        predictor ??= new RuleBasedPriorityPredictor();
        INotificationService notifications = new FakeNotificationService();
        return new CaseService(cases, customers, categories, predictor, notifications);
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
    public async Task DeleteAsync_RemovesCase()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        SeedCustomer(customers, 1);
        SeedCategory(categories, 1);

        var created = await svc.CreateAsync(new CreateCaseDto { Subject = "A", CustomerId = 1, CategoryId = 1 });
        Assert.NotNull(await svc.GetByIdAsync(created.Id));

        await svc.DeleteAsync(created.Id);
        Assert.Null(await svc.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        var svc = BuildService(out var cases, out var customers, out var categories);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteAsync(123));
    }

    /// <summary>No-op notification service for CaseService tests.</summary>
    private class FakeNotificationService : INotificationService
    {
        public Task<int> GenerateOverdueAsync() => Task.FromResult(0);
        public Task<int> NotifyResolvedAsync(Case caseEntity) => Task.FromResult(0);
        public Task<IReadOnlyList<NotificationDto>> GetAllAsync() => Task.FromResult<IReadOnlyList<NotificationDto>>(Array.Empty<NotificationDto>());
        public Task<NotificationSummaryDto> GetSummaryAsync() => Task.FromResult(new NotificationSummaryDto());
        public Task<bool> MarkReadAsync(int id) => Task.FromResult(false);
        public Task<int> MarkAllReadAsync() => Task.FromResult(0);
    }
}
