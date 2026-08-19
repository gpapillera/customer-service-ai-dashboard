using CustomerService.Domain.Entities;
using CustomerService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Verifies the EF Core global query filters added in Task A3 hide soft-deleted
/// <see cref="Customer"/> and <see cref="Case"/> rows from normal queries while
/// still allowing them to be retrieved with <c>IgnoreQueryFilters()</c>.
/// </summary>
public class SoftDeleteFilterTests
{
    private static AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Customers_QueryFilter_HidesSoftDeletedButIgnoreFiltersShowsThem()
    {
        using var ctx = NewContext();
        ctx.Customers.Add(new Customer { Id = 1, Name = "Alive", Email = "a@e.com", IsDeleted = false });
        ctx.Customers.Add(new Customer { Id = 2, Name = "Gone", Email = "g@e.com", IsDeleted = true });
        ctx.SaveChanges();

        // Default query respects the global filter: only the live row.
        var visible = ctx.Customers.ToList();
        Assert.Single(visible);
        Assert.Equal(1, visible[0].Id);

        // Ignoring filters surfaces the soft-deleted row too.
        var all = ctx.Customers.IgnoreQueryFilters().ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.Id == 2 && c.IsDeleted);
    }

    [Fact]
    public void Cases_QueryFilter_HidesSoftDeletedButIgnoreFiltersShowsThem()
    {
        using var ctx = NewContext();
        ctx.Cases.Add(new Case
        {
            Id = 1,
            Subject = "Open",
            CustomerId = 1,
            CategoryId = 1,
            Status = CaseStatus.New,
            Priority = Priority.Low,
            IsDeleted = false,
        });
        ctx.Cases.Add(new Case
        {
            Id = 2,
            Subject = "Closed",
            CustomerId = 1,
            CategoryId = 1,
            Status = CaseStatus.New,
            Priority = Priority.Low,
            IsDeleted = true,
        });
        ctx.SaveChanges();

        var visible = ctx.Cases.ToList();
        Assert.Single(visible);
        Assert.Equal(1, visible[0].Id);

        var all = ctx.Cases.IgnoreQueryFilters().ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.Id == 2 && c.IsDeleted);
    }
}
